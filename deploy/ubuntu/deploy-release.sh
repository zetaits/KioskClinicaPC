#!/usr/bin/env bash
# Run through sudo after uploading a package built by deploy-server-vps.ps1.
set -Eeuo pipefail

release_id=${1:-}
expected_sha=${2:-}
if [[ ! $release_id =~ ^[0-9]{14}-[0-9a-f]{16}$ || ! $expected_sha =~ ^[0-9a-f]{64}$ ]]; then
    echo 'Invalid release ID or SHA-256.' >&2
    exit 2
fi
if (( EUID != 0 )); then
    echo 'This script must run with sudo.' >&2
    exit 2
fi

base=/opt/kiosk-server
releases=$base/releases
app=$base/app
upload=/home/ubuntu/kiosk-server-$release_id.tar.gz
archive=$releases/$release_id.tar.gz
incoming=$releases/.incoming-$release_id
new_release=$releases/$release_id
next_link=$base/.app-next-$release_id
rollback_link=$base/.app-rollback-$release_id
state=/var/lib/kiosk-server
backup_root=/var/backups/kiosk-server
backup_tmp=$backup_root/.previous-$release_id.tar.gz
backup=

command -v python3 >/dev/null
command -v curl >/dev/null
command -v sha256sum >/dev/null
command -v flock >/dev/null
command -v tar >/dev/null
[[ ! -L $base && ! -L $releases && ! -L $state ]] || {
    echo 'Installation, release, and state roots must not be symlinks.' >&2
    exit 1
}
exec 9>"$base/.deploy.lock"
flock -n 9 || { echo 'Another server deployment is running.' >&2; exit 1; }
[[ -f $upload && ! -L $upload ]] || { echo 'Upload missing or is a symlink.' >&2; exit 1; }
[[ -d $app || -L $app ]] || { echo 'Current application is missing.' >&2; exit 1; }
install -d -o root -g root -m 755 "$releases"
for path in "$archive" "$incoming" "$new_release" "$next_link" "$rollback_link"; do
    [[ ! -e $path && ! -L $path ]] || { echo "Already exists: $path" >&2; exit 1; }
done

cleanup_staging() {
    local result=$?
    trap - EXIT
    set +e
    if (( result != 0 )); then
        rm -rf -- "$incoming"
        rm -f -- "$archive" "$upload" "/home/ubuntu/kiosk-server-deploy-$release_id.sh" \
            "/home/ubuntu/kiosk-server-preflight-$release_id.py"
    fi
    exit "$result"
}
trap cleanup_staging EXIT

# Copy the untrusted user-owned upload before hashing and extracting it as root.
install -o root -g root -m 600 "$upload" "$archive"
actual_sha=$(sha256sum "$archive")
actual_sha=${actual_sha%% *}
[[ $actual_sha == "$expected_sha" ]] || { echo 'Package SHA-256 mismatch.' >&2; exit 1; }

install -d -o root -g root -m 755 "$incoming"
python3 - "$archive" "$incoming" <<'PY'
import json
import hashlib
import pathlib
import shutil
import sys
import tarfile

archive = pathlib.Path(sys.argv[1])
destination = pathlib.Path(sys.argv[2])
seen = set()
with tarfile.open(archive, "r:gz") as package:
    members = package.getmembers()
    for member in members:
        name = member.name
        path = pathlib.PurePosixPath(name)
        parts = name.rstrip("/").split("/")
        if parts and parts[0] == ".":
            parts = parts[1:]
        if (not name or path.is_absolute() or "\\" in name or
                any(part in ("..", ".", "") for part in parts) or
                any(ord(char) < 32 for char in name) or
                not (member.isdir() or member.isfile())):
            raise SystemExit(f"Unsafe archive entry: {name!r}")
        normalized = str(path)
        if normalized == ".":
            if not member.isdir():
                raise SystemExit("Unsafe archive root")
            continue
        if normalized in seen:
            raise SystemExit(f"Duplicate archive entry: {name!r}")
        seen.add(normalized)
    for member in members:
        path = pathlib.PurePosixPath(member.name)
        if str(path) == ".":
            continue
        target = destination.joinpath(*path.parts)
        if member.isdir():
            target.mkdir(parents=True, exist_ok=True, mode=0o755)
        else:
            target.parent.mkdir(parents=True, exist_ok=True, mode=0o755)
            with package.extractfile(member) as source, target.open("xb") as output:
                shutil.copyfileobj(source, output)
            target.chmod(0o755 if member.mode & 0o111 else 0o644)

if not (destination / "Kiosk.Server.dll").is_file():
    raise SystemExit("Kiosk.Server.dll is missing")
with (destination / "release-info.json").open(encoding="utf-8-sig") as file:
    release_info = json.load(file)
with (destination / "Kiosk.Server.dll").open("rb") as file:
    dll_hash = hashlib.file_digest(file, "sha256").hexdigest()
if release_info.get("dllSha256") != dll_hash:
    raise SystemExit("DLL SHA-256 does not match release-info.json")
with (destination / "appsettings.json").open(encoding="utf-8-sig") as file:
    settings = json.load(file)["Kiosk"]
for name, directory in {
    "DataDir": "data", "AssetsDir": "assets", "InstallersDir": "installers",
    "SetupDir": "setups", "UpdatesDir": "updates"
}.items():
    expected = f"/var/lib/kiosk-server/{directory}"
    if settings.get(name) != expected:
        raise SystemExit(f"{name} must be {expected}")
PY

mv -T "$incoming" "$new_release"

old_target=
rollback_needed=0
migrated=0
health() {
    local body
    body=$(curl --fail --silent --show-error --max-time 3 http://127.0.0.1:5080/health) || return 1
    [[ $body == '{"status":"ok"}' ]] && systemctl is-active --quiet kiosk-server
}
ready() {
    local body pid cwd
    health || return 1
    body=$(curl --fail --silent --show-error --max-time 3 http://127.0.0.1:5080/health/ready) || return 1
    [[ $body == '{"status":"ok"}' ]] || return 1
    pid=$(systemctl show kiosk-server --property=MainPID --value) || return 1
    [[ $pid =~ ^[1-9][0-9]*$ ]] || return 1
    cwd=$(readlink -f "/proc/$pid/cwd") || return 1
    [[ $cwd == "$new_release" ]]
}
on_exit() {
    local result=$1
    trap - EXIT
    set +e
    rm -f -- "$backup_tmp"
    if (( result != 0 && rollback_needed )); then
        echo 'Deployment failed; restoring the previous release.' >&2
        if [[ -L $app ]]; then
            ln -s "$old_target" "$rollback_link"
            mv -Tf "$rollback_link" "$app"
        elif [[ ! -e $app && $migrated == 1 ]]; then
            ln -s "$old_target" "$app"
        fi
        if systemctl restart kiosk-server; then
            for (( attempt=1; attempt<=20; attempt++ )); do
                if health; then
                    echo 'Previous release is healthy again.' >&2
                    break
                fi
                sleep 1
            done
        fi
        [[ -z $backup ]] || echo "Data snapshot for manual recovery: $backup" >&2
    fi
    if (( result != 0 )); then
        if [[ ! -L $app ]] || [[ $(readlink -f "$app") != "$new_release" ]]; then
            rm -rf -- "$new_release"
        fi
        rm -rf -- "$incoming"
        rm -f -- "$archive" "$upload" "/home/ubuntu/kiosk-server-deploy-$release_id.sh" \
            "/home/ubuntu/kiosk-server-preflight-$release_id.py"
    fi
    exit "$result"
}
trap 'on_exit $?' EXIT

if [[ -L $app ]]; then
    old_target=$(readlink -f "$app")
    [[ -d $old_target && $old_target == "$releases/"* ]] || {
        echo 'Current app symlink must point to a release inside the installation.' >&2
        exit 1
    }
else
    baseline=$releases/baseline-$(date -u +%Y%m%d%H%M%S)
    [[ ! -e $baseline ]] || { echo 'Baseline release already exists.' >&2; exit 1; }
    old_target=$baseline
fi

[[ ! -L $backup_root ]] || { echo 'Backup directory must not be a symlink.' >&2; exit 1; }
install -d -o root -g root -m 700 "$backup_root"
for name in data assets installers setups updates; do
    if [[ $name != data && ! -e $state/$name && ! -L $state/$name ]]; then
        install -d -o kiosk-server -g kiosk-server -m 750 "$state/$name"
    fi
    [[ -d $state/$name && ! -L $state/$name ]] || {
        echo "Persistent directory missing or linked: $state/$name" >&2
        exit 1
    }
done
size_kb=$(du -skc "$state"/{data,assets,installers,setups,updates} | tail -n 1 | awk '{print $1}')
free_kb=$(df -Pk "$backup_root" | awk 'END {print $4}')
[[ $size_kb =~ ^[0-9]+$ && $free_kb =~ ^[0-9]+$ ]] || {
    echo 'Cannot measure backup space.' >&2
    exit 1
}
if (( free_kb < size_kb + size_kb / 10 + 102400 )); then
    echo 'Not enough free space for a complete data snapshot.' >&2
    exit 1
fi
rollback_needed=1
systemctl stop kiosk-server
tar -C "$state" -czf "$backup_tmp" data assets installers setups updates
tar -tzf "$backup_tmp" >/dev/null
backup_hash=$(sha256sum "$backup_tmp")
backup_hash=${backup_hash%% *}
backup=$backup_root/previous-$backup_hash.tar.gz
chmod 600 "$backup_tmp"
mv -Tf "$backup_tmp" "$backup"
for previous in "$backup_root"/previous-*.tar.gz; do
    [[ $previous == "$backup" ]] || rm -f -- "$previous"
done
state_before=$(sha256sum "$state/data/KioskConfig.json" "$state/data/panel.json" "$state/data/events.json" | sha256sum)
if [[ ! -L $app ]]; then
    mv -T "$app" "$old_target"
    migrated=1
    ln -s "$old_target" "$app"
fi
ln -s "$new_release" "$next_link"
mv -Tf "$next_link" "$app"
systemctl start kiosk-server
for (( attempt=1; attempt<=20; attempt++ )); do
    if ready; then
        state_after=$(sha256sum "$state/data/KioskConfig.json" "$state/data/panel.json" "$state/data/events.json" | sha256sum)
        [[ $state_after == "$state_before" ]] || {
            echo 'Persistent content changed during startup.' >&2
            exit 1
        }
        rollback_needed=0
        for path in "$releases"/*; do
            [[ -d $path && ! -L $path ]] || continue
            [[ $path == "$new_release" || $path == "$old_target" ]] && continue
            name=${path##*/}
            if [[ $name =~ ^([0-9]{14}-[0-9a-f]{16}|baseline-[0-9]{14})$ ]]; then
                rm -rf -- "$path" || echo "Could not remove old release: $path" >&2
            fi
        done
        for path in "$releases"/*.tar.gz; do
            name=${path##*/}
            if [[ $name =~ ^[0-9]{14}-[0-9a-f]{16}\.tar\.gz$ ]]; then
                rm -f -- "$path" || echo "Could not remove package: $path" >&2
            fi
        done
        rm -f -- "$upload" "/home/ubuntu/kiosk-server-deploy-$release_id.sh" \
            "/home/ubuntu/kiosk-server-preflight-$release_id.py" || true
        echo "Deployed $release_id; previous release: $old_target; one data snapshot: $backup"
        exit 0
    fi
    sleep 1
done
echo 'New release failed the local health check.' >&2
exit 1
