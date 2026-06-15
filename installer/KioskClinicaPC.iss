; Inno Setup 6 - Instalador KioskClinicaPC
; Empaqueta la salida de `dotnet publish` (self-contained win-x64) en un Setup.exe.
; El autostart NO se gestiona aqui: lo hace la propia app (HKCU\...\Run) en su primer arranque,
; por eso al final del instalador se lanza la app una vez (casilla "Ejecutar...").

#define MyAppName "Kiosko Clinica PC"
; Permite sobreescribir la version desde linea de comandos: ISCC /DMyAppVersion=1.1.0
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "Clinica PC"
#define MyAppExeName "KioskClinicaPC.exe"

; Carpeta con el resultado de `dotnet publish` (ruta relativa a este .iss).
#define PublishDir "..\publish"

[Setup]
AppId={{A7E3C9F1-2B4D-4E6A-9C8B-1F0D5E2A6B33}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\KioskClinicaPC
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Program Files requiere admin para escribir.
PrivilegesRequired=admin
OutputDir=Output
OutputBaseFilename=Setup-KioskClinicaPC-{#MyAppVersion}
SetupIconFile=..\Assets\clinicapc-logo.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Upgrade in-place: al instalar una version nueva encima (mismo AppId), cierra el kiosko
; en ejecucion para liberar los archivos, actualiza y lo relanza via [Run]. La config del
; usuario en %LOCALAPPDATA% se conserva (UninstallDelete solo corre en desinstalacion real).
CloseApplications=yes
RestartApplications=yes
; Cierra tambien procesos que no respondan al mensaje de cierre (kiosko fullscreen).
CloseApplicationsFilter=*.exe
; Solo PCs x64.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"

[Dirs]
; Carpeta machine-wide para el auto-update: la app (usuario kiosko) descarga aqui el Setup y la
; tarea SYSTEM lo aplica. Users=Modify para que el kiosko (no admin) pueda escribir.
Name: "{commonappdata}\KioskClinicaPC\updates"; Permissions: users-modify

[Files]
; Copia TODO el contenido del publish (exe, dlls, runtime, Assets\Brands, Assets\SpecImages...).
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Aplicador de updates. Vive en ProgramData (NO en {app}) y con onlyifdoesntexist: asi un upgrade
; en silencio NO lo sobrescribe mientras la tarea lo esta ejecutando (evita bloqueo de archivo).
Source: "updater.cmd"; DestDir: "{commonappdata}\KioskClinicaPC"; Flags: onlyifdoesntexist uninsremovereadonly

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Desinstalar {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Registra la tarea SYSTEM que aplica los updates de madrugada (sin UAC). Se (re)crea en cada
; instalacion. Corre diariamente a las 04:00; si el PC esta apagado a esa hora, queda el boton
; manual "Buscar actualizaciones" + reinicio en Settings.
Filename: "{sys}\schtasks.exe"; Parameters: "/Create /F /TN ""KioskClinicaPC Updater"" /RU SYSTEM /RL HIGHEST /SC DAILY /ST 04:00 /TR ""{commonappdata}\KioskClinicaPC\updater.cmd"""; Flags: runhidden; StatusMsg: "Configurando actualizaciones automaticas..."
; Lanza la app al terminar -> dispara el auto-registro de arranque (HKCU\...\Run) de la propia app.
; skipifsilent: en un upgrade silencioso (tarea SYSTEM) NO se relanza aqui (seria sesion 0); el
; reinicio del updater.cmd + autostart lo trae de vuelta en la sesion del usuario.
Filename: "{app}\{#MyAppExeName}"; Description: "Ejecutar {#MyAppName} ahora"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Quita la tarea de actualizacion al desinstalar.
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN ""KioskClinicaPC Updater"""; Flags: runhidden; RunOnceId: "DelUpdaterTask"

[UninstallDelete]
; Limpia la carpeta de instalacion, la config del usuario y los datos machine-wide del updater
; (desinstalacion completa).
; OJO: en una desinstalacion elevada, {localappdata} resuelve al perfil del usuario que
; aprueba el UAC. En kioscos donde el usuario logueado es admin (caso normal) coincide.
Type: filesandordirs; Name: "{app}"
Type: filesandordirs; Name: "{localappdata}\KioskClinicaPC"
Type: filesandordirs; Name: "{commonappdata}\KioskClinicaPC"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    // Autostart: lo crea la app en runtime (HKCU\...\Run, valor "KioskHardwareDisplay").
    // Inno no lo conoce, hay que borrarlo aqui o queda huerfano apuntando a un exe borrado.
    RegDeleteValue(HKCU, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Run', 'KioskHardwareDisplay');
    // Restaura el Administrador de tareas por si la app fue matada sin salir limpia.
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Policies\System', 'DisableTaskMgr');
  end;
end;
