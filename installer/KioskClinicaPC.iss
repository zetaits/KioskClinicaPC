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
SetupIconFile=..\src\Kiosk.Client\Assets\clinicapc-logo.ico
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
Source: "{#AgentPublishDir}\*"; DestDir: "{app}\Agent"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MaintenancePublishDir}\KioskMaintenanceRunner.exe"; DestDir: "{app}\Agent\Maintenance"; Flags: ignoreversion
; Aplicador de updates. Vive en ProgramData (NO en {app}) y con onlyifdoesntexist: asi un upgrade
; en silencio NO lo sobrescribe mientras la tarea lo esta ejecutando (evita bloqueo de archivo).
Source: "updater.cmd"; DestDir: "{commonappdata}\KioskClinicaPC"; Flags: onlyifdoesntexist uninsremovereadonly

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Desinstalar {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; El servicio fija aquí el origen HTTPS en su primer handshake con el cliente instalado. Se conserva en
; upgrades y se elimina en una desinstalación completa.
Root: HKLM; Subkey: "SOFTWARE\ClinicaPC\Kiosk"; ValueType: string; ValueName: "InstallerServerUrl"; ValueData: ""; Flags: createvalueifdoesntexist uninsdeletevalue

[Run]
; Registra la tarea SYSTEM que aplica los updates de madrugada (sin UAC). Se (re)crea en cada
; instalacion. Corre diariamente a las 04:00; si el PC esta apagado a esa hora, queda el boton
; manual "Buscar actualizaciones" + reinicio en Settings.
Filename: "{sys}\schtasks.exe"; Parameters: "/Create /F /TN ""KioskClinicaPC Updater"" /RU SYSTEM /RL HIGHEST /SC DAILY /ST 04:00 /TR ""{commonappdata}\KioskClinicaPC\updater.cmd"""; Flags: runhidden; StatusMsg: "Configurando actualizaciones automaticas..."
; Agente privilegiado de instalaciones remotas. La app WPF solo le entrega IDs autorizados por pipe.
Filename: "{sys}\sc.exe"; Parameters: "create KioskClinicaPCInstallerAgent binPath= ""{app}\Agent\KioskInstallerAgent.exe"" start= auto DisplayName= ""KioskClinicaPC Installer Agent"""; Flags: runhidden; StatusMsg: "Configurando agente de instalaciones..."
Filename: "{sys}\sc.exe"; Parameters: "config KioskClinicaPCInstallerAgent binPath= ""{app}\Agent\KioskInstallerAgent.exe"" start= auto"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "failure KioskClinicaPCInstallerAgent reset= 86400 actions= restart/5000/restart/15000/restart/30000"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "start KioskClinicaPCInstallerAgent"; Flags: runhidden
; Lanza la app al terminar -> dispara el auto-registro de arranque (HKCU\...\Run) de la propia app.
; skipifsilent: en un upgrade silencioso (tarea SYSTEM) NO se relanza aqui (seria sesion 0); el
; reinicio del updater.cmd + autostart lo trae de vuelta en la sesion del usuario.
Filename: "{app}\{#MyAppExeName}"; Description: "Ejecutar {#MyAppName} ahora"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop KioskClinicaPCInstallerAgent"; Flags: runhidden; RunOnceId: "StopInstallerAgent"
Filename: "{sys}\sc.exe"; Parameters: "delete KioskClinicaPCInstallerAgent"; Flags: runhidden; RunOnceId: "DelInstallerAgent"
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
const
  ProvisionedServerUrl = '{#DefaultServerUrl}';
  ProvisionedServerApiKey = '{#DefaultServerApiKey}';

var
  ServerProvisioningWritten: Boolean;

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

  { Se escribe en Program Files. El kiosco, ya bajo el usuario interactivo correcto, lo aplica solo si
    todavía no existe su KioskSettings.json. Así una elevación con otra cuenta admin no configura el
    perfil equivocado y los upgrades nunca pisan contraseña, identidad ni ajustes existentes. }
  ProvisioningPath := ExpandConstant('{app}\KioskProvisioning.json');
  Json := '{' + #13#10 +
    '  "ServerUrl": "' + JsonEscape(NormalizedServerUrl()) + '",' + #13#10 +
    '  "ServerApiKey": "' + JsonEscape(ProvisionedServerApiKey) + '"' + #13#10 +
    '}' + #13#10;
  Result := SaveStringToFile(ProvisioningPath, Json, False);
end;

function TestProvisionedServer(var Detail: String): Boolean;
var
  Http: Variant;
  StatusCode: Integer;
begin
  Result := False;
  Detail := '';
  try
    Http := CreateOleObject('WinHttp.WinHttpRequest.5.1');
    Http.SetTimeouts(5000, 5000, 5000, 10000);
    Http.Open('GET', NormalizedServerUrl() + '/api/config/version', False);
    Http.SetRequestHeader('X-Api-Key', ProvisionedServerApiKey);
    Http.Send('');
    StatusCode := Http.Status;
    Result := StatusCode = 200;
    if not Result then
    begin
      if StatusCode = 401 then
        Detail := 'La clave de conexión fue rechazada por el servidor.'
      else
        Detail := Format('El servidor respondió con el código HTTP %d.', [StatusCode]);
    end;
  except
    Detail := 'No se pudo contactar con el servidor. Comprueba la conexión a Internet e inténtalo de nuevo.';
  end;
end;

procedure NotifyServerConnection();
var
  Detail: String;
begin
  if WizardSilent or (NormalizedServerUrl() = '') or (ProvisionedServerApiKey = '') then
    exit;

  if not ServerProvisioningWritten then
  begin
    MsgBox('No se pudo guardar la configuración automática del servidor.' + #13#10#13#10 +
      'La instalación continuará, pero será necesario configurar la conexión desde Ajustes.',
      mbError, MB_OK);
    exit;
  end;

  if TestProvisionedServer(Detail) then
    MsgBox('El kiosco se ha configurado y conectado correctamente al servidor.' + #13#10 +
      NormalizedServerUrl(), mbInformation, MB_OK)
  else
    MsgBox('El kiosco se ha configurado, pero no ha podido verificar la conexión con el servidor.' + #13#10#13#10 +
      Detail + #13#10#13#10 +
      'La instalación continuará y el kiosco volverá a intentarlo automáticamente al iniciarse.',
      mbError, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop KioskClinicaPCInstallerAgent', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(3000);
  end;
  if CurStep = ssPostInstall then
  begin
    ServerProvisioningWritten := WriteServerProvisioning();
    NotifyServerConnection();
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
