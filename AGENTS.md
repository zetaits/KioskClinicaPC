# AGENTS.md

## Workflow

- Keep changes scoped to the task and preserve unrelated working-tree changes.
- Inspect relevant code before editing; do not invent project structure or commands.
- Never read or expose `.env` or `.env.*` unless the user explicitly requests it.
- After changes, run the smallest relevant verification and report what could not run.

## Projects and build

`KioskClinicaPC.sln` contains the WPF client (`src/Kiosk.Client`), ASP.NET Core server (`src/Kiosk.Server`), shared models (`src/Kiosk.Shared`), Windows installer agent and helpers (`src/Kiosk.InstallerAgent`, `src/Kiosk.InstallerCore`, `src/Kiosk.SetupHelper`, `src/Kiosk.UpdateRunner`, `src/Kiosk.MaintenanceRunner`), two test projects and `tools/Kiosk.ReleaseTool`. The client assembly remains `KioskClinicaPC.exe`.

Use the user-local .NET 10 SDK:

```powershell
& "C:\Users\zits\.dotnet\dotnet.exe" build "C:\Users\zits\Documents\Proyectos\KioskClinicaPC\KioskClinicaPC.sln" -nologo -clp:ErrorsOnly
```

`C:\Program Files\dotnet\dotnet.exe` has runtimes only; VS2022 MSBuild cannot resolve the SDK. `dotnet` is not on the Bash PATH. The working SDK is `C:\Users\zits\.dotnet\sdk\10.0.100` (`global.json` permits later .NET 10 feature bands). Client output: `src\Kiosk.Client\bin\Debug\net10.0-windows\KioskClinicaPC.exe`.

## Running and deployment

- Running the WPF app starts fullscreen kiosk protection even in Debug: it hides the taskbar and disables Task Manager. Exit through Settings → “Salir del kiosko” or `Ctrl+Shift+K`; killing the process leaves the desktop locked. Debug skips the keyboard hook and autostart, so `Alt+Tab` works.
- Development server: `& "C:\Users\zits\.dotnet\dotnet.exe" run --project src\Kiosk.Server`. It uses the ASP.NET Core 10 runtime and prints its local URL.
- Public panel: `https://panel.clinicapc.es`. SSH uses the technical hostname `vps-9c7061ff.vps.ovh.net`; the Caddy example also accepts it over HTTPS.
- VPS update: run `& .\deploy-server-vps.ps1` from the repo root, then check `https://panel.clinicapc.es/health/ready`. The command preflights, packages, backs up persistent data, switches releases and rolls back the binary on failure. `-CheckOnly` is read-only; details and recovery are in `docs/ACTUALIZAR-PANEL-VPS.txt`. Initial VPS setup and server configuration are in `docs/SERVIDOR.md`.
- `/health` checks liveness; `/health/ready` checks persisted state. Both are unauthenticated and return `{"status":"ok"}` when healthy.

## Access and persistence

- Client Settings: three clicks in the top-right corner or `Ctrl+Shift+S`, then the locally configured password. Free edit mode is disabled when a server is configured; shared content is edited in the web panel. Prices, condition and detected specs remain per machine.
- Client `%LOCALAPPDATA%\KioskClinicaPC\`: `KioskConfig.json` (content and server cache), `KioskSettings.json` (behavior, password hash, `ServerUrl`, API key), `KioskHardware.json` (last detection). Empty `ServerUrl` means local-only mode. If the server is unavailable, the kiosk uses its cached content.
- The public kiosk installer receives `KIOSK_SERVER_API_KEY` from the GitHub `production` secret and fills the server URL/key when an existing profile has neither; it preserves other settings. The key is extractable from that public installer by design. Never put its value in Git or these instructions.
- VPS `/var/lib/kiosk-server/`: `data/` (content, events, panel hash, fleet and job metadata, Data Protection keys), `assets/` (image library), `installers/`, `setups/` and `updates/` (private package stores). Configuration and secrets stay under `/etc/kiosk-server/`. The deploy script keeps one prior data snapshot under `/var/backups/kiosk-server/`.
- Admin panel: `/login`, cookie authentication for one manager. No credentials belong in project instructions. API endpoints use `Kiosk__ApiKey` in production; the panel password hash lives in `data/panel.json`.

## Architecture pointers

- WPF uses custom MVVM (`BaseViewModel`), Newtonsoft.Json and Serilog. `MainWindow.NavigateToScreen` swaps four screens; inline editing uses `Controls/Editable` and `Controls/InlineEdit`.
- `RemoteConfigRepository` merges server-owned shop, slides, texts and images over local price, condition and hardware through `SharedContent.ApplyServerToLocal`. `SyncClient` handles SignalR attract timing and content reload; `FleetClient` passes only job IDs/tokens to the privileged installer agent.
- `src/Kiosk.Server/Program.cs` wires the API, SignalR hubs and Blazor panel. Server stores are file-backed; remote packages are verified before execution. The QR page is served at `/ficha/`; GitHub Pages only redirects older printed QR codes. Details: `docs/SERVIDOR.md` and `docs/README.md`.
