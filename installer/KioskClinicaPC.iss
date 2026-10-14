; Inno Setup 6 - Instalador KioskClinicaPC
; Empaqueta la salida de `dotnet publish` (self-contained win-x64) en un Setup.exe.
; El autostart NO se gestiona aqui: lo hace la propia app (HKCU\...\Run) en su primer arranque,
; por eso al final del instalador se lanza la app una vez (casilla "Ejecutar...").

#define MyAppName "Kiosko Clinica PC"
; Permite sobreescribir la version desde linea de comandos: ISCC /DMyAppVersion=1.1.0
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef DefaultServerUrl
  #define DefaultServerUrl ""
#endif
#ifndef DefaultServerApiKey
  #define DefaultServerApiKey ""
#endif
#define MyAppPublisher "Clinica PC"
#define MyAppExeName "KioskClinicaPC.exe"

; Carpeta con el resultado de `dotnet publish` (ruta relativa a este .iss).
#define PublishDir "..\publish"
#define AgentPublishDir "..\publish-agent"
#define MaintenancePublishDir "..\publish-maintenance"
#define UpdateRunnerPublishDir "..\publish-update-runner"

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
; El uso de {localappdata} está limitado a la limpieza explícita de una desinstalación real y se
; conserva deliberadamente; no se escriben ajustes de usuario durante la instalación elevada.
UsedUserAreasWarning=no
OutputDir=Output
OutputBaseFilename=Setup-KioskClinicaPC-{#MyAppVersion}
SetupIconFile=..\src\Kiosk.Client\Assets\clinicapc-logo.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Upgrade in-place: al instalar una version nueva encima (mismo AppId), cierra el kiosko
; en ejecucion para liberar los archivos, actualiza y lo relanza via [Run]. La config del
; usuario en %LOCALAPPDATA% se conserva (UninstallDelete solo corre en desinstalacion real).
CloseApplications=yes
RestartApplications=yes
RestartIfNeededByRun=no
; Cierra tambien procesos que no respondan al mensaje de cierre (kiosko fullscreen).
CloseApplicationsFilter=*.exe
; Solo PCs x64.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Uninstallable=ShouldInstallKiosk

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Types]
Name: "kioskonly"; Description: "Kiosk"

[Components]
Name: "kiosk"; Description: "Instalar Kiosko Clínica PC"; Types: kioskonly; Flags: fixed

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"; Components: kiosk

[Dirs]
; Carpeta machine-wide para el auto-update: la app (usuario kiosko) descarga aqui el Setup y la
; tarea SYSTEM lo aplica. Users=Modify para que el kiosko (no admin) pueda escribir.
Name: "{commonappdata}\KioskClinicaPC\updates"; Permissions: users-modify; Components: kiosk

[Files]
; Copia TODO el contenido del publish (exe, dlls, runtime, Assets\Brands, Assets\SpecImages...).
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: kiosk
Source: "{#AgentPublishDir}\*"; DestDir: "{app}\Agent"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: kiosk
Source: "{#MaintenancePublishDir}\KioskMaintenanceRunner.exe"; DestDir: "{app}\Agent\Maintenance"; Flags: ignoreversion; Components: kiosk
Source: "{#UpdateRunnerPublishDir}\KioskUpdateRunner.exe"; DestDir: "{app}\Agent\Update"; Flags: ignoreversion; Components: kiosk
; Aplicador de updates. Vive en ProgramData (NO en {app}) y con onlyifdoesntexist: asi un upgrade
; en silencio NO lo sobrescribe mientras la tarea lo esta ejecutando (evita bloqueo de archivo).
Source: "updater.cmd"; DestDir: "{commonappdata}\KioskClinicaPC"; Flags: onlyifdoesntexist uninsremovereadonly; Components: kiosk

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Components: kiosk
Name: "{group}\Desinstalar {#MyAppName}"; Filename: "{uninstallexe}"; Components: kiosk
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; Components: kiosk

[Registry]
; El servicio fija aquí el origen HTTPS en su primer handshake con el cliente instalado. Se conserva en
; upgrades y se elimina en una desinstalación completa.
Root: HKLM; Subkey: "SOFTWARE\ClinicaPC\Kiosk"; ValueType: string; ValueName: "InstallerServerUrl"; ValueData: ""; Flags: createvalueifdoesntexist uninsdeletevalue; Components: kiosk

[Run]
; Retira la tarea heredada que consultaba GitHub directamente. La release puente puede borrar su propia
; tarea porque updater.cmd ya lanzó el Setup como un proceso independiente.
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN ""KioskClinicaPC Updater"""; Flags: runhidden; Components: kiosk
; El runner firmado vuelve a validar el trabajo y su ventana con la VPS antes de ejecutar nada.
Filename: "{sys}\schtasks.exe"; Parameters: "/Create /F /TN ""KioskClinicaPC Update Check"" /RU SYSTEM /RL HIGHEST /SC MINUTE /MO 5 /TR ""{app}\Agent\Update\KioskUpdateRunner.exe"""; Flags: runhidden; StatusMsg: "Configurando actualizaciones administradas..."; Components: kiosk
Filename: "{sys}\schtasks.exe"; Parameters: "/Create /F /TN ""KioskClinicaPC Update Startup"" /RU SYSTEM /RL HIGHEST /SC ONSTART /DELAY 0001:00 /TR ""{app}\Agent\Update\KioskUpdateRunner.exe"""; Flags: runhidden; Components: kiosk
; Agente privilegiado de instalaciones remotas. La app WPF solo le entrega IDs autorizados por pipe.
Filename: "{sys}\sc.exe"; Parameters: "create KioskClinicaPCInstallerAgent binPath= ""{app}\Agent\KioskInstallerAgent.exe"" start= auto DisplayName= ""KioskClinicaPC Installer Agent"""; Flags: runhidden; StatusMsg: "Configurando agente de instalaciones..."; Components: kiosk
Filename: "{sys}\sc.exe"; Parameters: "config KioskClinicaPCInstallerAgent binPath= ""{app}\Agent\KioskInstallerAgent.exe"" start= auto"; Flags: runhidden; Components: kiosk
Filename: "{sys}\sc.exe"; Parameters: "failure KioskClinicaPCInstallerAgent reset= 86400 actions= restart/5000/restart/15000/restart/30000"; Flags: runhidden; Components: kiosk
Filename: "{sys}\sc.exe"; Parameters: "start KioskClinicaPCInstallerAgent"; Flags: runhidden; Components: kiosk
; El asistente registra el autostart para el usuario al verificar Kiosk. Las actualizaciones
; silenciosas no abren la app en la sesión SYSTEM.
Filename: "{app}\{#MyAppExeName}"; Description: "Ejecutar {#MyAppName} ahora"; Flags: nowait postinstall skipifsilent runasoriginaluser; Components: kiosk

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop KioskClinicaPCInstallerAgent"; Flags: runhidden; RunOnceId: "StopInstallerAgent"
Filename: "{sys}\sc.exe"; Parameters: "delete KioskClinicaPCInstallerAgent"; Flags: runhidden; RunOnceId: "DelInstallerAgent"
; Quita la tarea de actualizacion al desinstalar.
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN ""KioskClinicaPC Updater"""; Flags: runhidden; RunOnceId: "DelUpdaterTask"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN ""KioskClinicaPC Update Check"""; Flags: runhidden; RunOnceId: "DelManagedUpdaterTask"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN ""KioskClinicaPC Update Startup"""; Flags: runhidden; RunOnceId: "DelManagedUpdaterStartupTask"

[UninstallDelete]
; Limpia la carpeta de instalacion, la config del usuario y los datos machine-wide del updater
; (desinstalacion completa).
; OJO: en una desinstalacion elevada, {localappdata} resuelve al perfil del usuario que
; aprueba el UAC. En kioscos donde el usuario logueado es admin (caso normal) coincide.
Type: filesandordirs; Name: "{app}"
Type: filesandordirs; Name: "{localappdata}\KioskClinicaPC"
Type: filesandordirs; Name: "{commonappdata}\KioskClinicaPC"

[Code]
const
  ProvisionedServerUrl = '{#DefaultServerUrl}';
  ProvisionedServerApiKey = '{#DefaultServerApiKey}';

var
  ServerProvisioningWritten: Boolean;

function ShouldInstallKiosk(): Boolean;
begin
  Result := WizardIsComponentSelected('kiosk');
end;

function JsonEscape(const Value: String): String;
begin
  Result := Value;
  StringChangeEx(Result, '\', '\\', True);
  StringChangeEx(Result, '"', '\"', True);
  StringChangeEx(Result, #13, '\r', True);
  StringChangeEx(Result, #10, '\n', True);
end;

function NormalizedServerUrl(): String;
begin
  Result := ProvisionedServerUrl;
  while (Length(Result) > 0) and (Result[Length(Result)] = '/') do
    Delete(Result, Length(Result), 1);
end;

function WriteServerProvisioning(): Boolean;
var
  ProvisioningPath, Json: String;
begin
  Result := False;
  if (NormalizedServerUrl() = '') or (ProvisionedServerApiKey = '') then
    exit;

  { Se escribe en Program Files. El kiosco, ya bajo el usuario interactivo correcto, lo aplica
    si su perfil aún no tiene servidor. Así una elevación con otra cuenta admin no configura el
    perfil equivocado y los upgrades conservan contraseña, identidad y demás ajustes. }
  ProvisioningPath := ExpandConstant('{app}\KioskProvisioning.json');
  Json := '{' + #13#10 +
    '  "ServerUrl": "' + JsonEscape(NormalizedServerUrl()) + '",' + #13#10 +
    '  "ServerApiKey": "' + JsonEscape(ProvisionedServerApiKey) + '"' + #13#10 +
    '}' + #13#10;
  Result := SaveStringToFile(ProvisioningPath, Json, False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if (CurStep = ssInstall) and ShouldInstallKiosk() then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop KioskClinicaPCInstallerAgent', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(3000);
  end;
  if CurStep = ssPostInstall then
  begin
    if ShouldInstallKiosk() then
    begin
      ServerProvisioningWritten := WriteServerProvisioning();
      if (ProvisionedServerApiKey <> '') and (not ServerProvisioningWritten) then
        Log('No se pudo guardar el aprovisionamiento de Kiosk; configura la conexión desde Ajustes.');
      { No blocking network dialogs after installation has started. }
    end;
  end;
end;

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
