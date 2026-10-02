"""Read-only checks before replacing the running kiosk server release."""

import json
import os
import pathlib
import pwd
import subprocess
import urllib.request


BASE = pathlib.Path("/opt/kiosk-server")
STATE = pathlib.Path("/var/lib/kiosk-server")
NAMES = {
    "DataDir": "data",
    "AssetsDir": "assets",
    "InstallersDir": "installers",
    "SetupDir": "setups",
    "UpdatesDir": "updates",
}


def fail(message):
    raise SystemExit(f"Preflight failed: {message}")


def systemctl(property_name):
    result = subprocess.run(
        ["systemctl", "show", "kiosk-server", f"--property={property_name}", "--value"],
        check=True, capture_output=True, text=True,
    )
    return result.stdout.strip()


def main():
    if os.geteuid() != 0:
        fail("run with sudo")
    app = BASE / "app"
    if not app.is_dir():
        fail("/opt/kiosk-server/app is missing")
    if subprocess.run(["systemctl", "is-active", "--quiet", "kiosk-server"]).returncode:
        fail("kiosk-server is not active")
    pid_text = systemctl("MainPID")
    if not pid_text.isdigit() or int(pid_text) == 0:
        fail("kiosk-server has no main process")
    pid = int(pid_text)
    if pathlib.Path(f"/proc/{pid}/cwd").resolve() != app.resolve():
        fail("running process is not using /opt/kiosk-server/app")
    command = pathlib.Path(f"/proc/{pid}/cmdline").read_bytes().split(b"\0")
    if not any(arg.endswith(b"/Kiosk.Server.dll") for arg in command):
        fail("running process is not Kiosk.Server.dll")
    unit_workdir = systemctl("WorkingDirectory")
    unit_exec = systemctl("ExecStart")
    if unit_workdir != str(app) or str(app / "Kiosk.Server.dll") not in unit_exec:
        fail("systemd unit does not use the expected app path; inspect it before deploying")
    runtimes = subprocess.run(
        ["/usr/bin/dotnet", "--list-runtimes"], check=True, capture_output=True, text=True,
    ).stdout
    if not any(line.startswith("Microsoft.AspNetCore.App 10.") for line in runtimes.splitlines()):
        fail("ASP.NET Core 10 runtime is missing")

    settings_file = app / "appsettings.json"
    try:
        settings = json.loads(settings_file.read_text(encoding="utf-8-sig")).get("Kiosk", {})
    except (OSError, ValueError) as error:
        fail(f"cannot read appsettings.json: {error}")
    if not isinstance(settings, dict):
        fail("appsettings.json has an invalid Kiosk section")
    environment = {}
    for item in pathlib.Path(f"/proc/{pid}/environ").read_bytes().split(b"\0"):
        key, separator, value = item.partition(b"=")
        if separator and key.startswith(b"Kiosk__") and key.endswith(b"Dir"):
            environment[key.decode("ascii")] = value.decode("utf-8")

    try:
        owner = pwd.getpwnam("kiosk-server").pw_uid
    except KeyError:
        fail("kiosk-server service account is missing")
    for setting, name in NAMES.items():
        expected = STATE / name
        configured = environment.get(f"Kiosk__{setting}") or settings.get(setting) or str(app / name)
        current = pathlib.Path(configured)
        if not current.is_absolute():
            current = app / current
        if current.resolve() != expected:
            if name == "data" or (current.exists() and any(current.iterdir())):
                fail(f"{setting} currently uses {current}; migrate its data to {expected} first")
            print(f"Preflight: unused {setting} path {current} will become {expected}")
        if not expected.exists() and name != "data" and (
            not current.exists() or not any(current.iterdir())
        ):
            print(f"Preflight: empty {name} directory will be created during deployment")
            continue
        if not expected.is_dir() or expected.is_symlink():
            fail(f"persistent directory {expected} is missing or is a symlink")
        mode = expected.stat()
        if mode.st_uid != owner or not mode.st_mode & 0o200:
            fail(f"persistent directory {expected} is not writable by kiosk-server")

    for name, expected_type in (("KioskConfig.json", dict), ("panel.json", dict), ("events.json", list)):
        path = STATE / "data" / name
        try:
            value = json.loads(path.read_text(encoding="utf-8-sig"))
        except (OSError, ValueError) as error:
            fail(f"cannot read valid {name}: {error}")
        if not isinstance(value, expected_type):
            fail(f"{name} has the wrong JSON structure")
        if name == "panel.json" and not value.get("PasswordHash"):
            fail("panel.json has no password hash")

    try:
        with urllib.request.urlopen("http://127.0.0.1:5080/health", timeout=3) as response:
            if response.status != 200 or json.load(response) != {"status": "ok"}:
                fail("current server health response is invalid")
    except OSError as error:
        fail(f"current server health check failed: {error}")

    release_info = app / "release-info.json"
    if release_info.is_file():
        try:
            release_id = json.loads(release_info.read_text(encoding="utf-8-sig")).get("releaseId", "unknown")
        except ValueError:
            release_id = "invalid manifest"
    else:
        release_id = "legacy installation"
    print(f"Preflight OK: active {release_id}; ASP.NET Core 10; persistent data under {STATE}")


if __name__ == "__main__":
    try:
        main()
    except (OSError, subprocess.CalledProcessError) as error:
        fail(str(error))
