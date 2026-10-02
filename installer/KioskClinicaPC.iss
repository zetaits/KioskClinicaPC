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
#ifndef DefaultInitialSetupKey
  #define DefaultInitialSetupKey ""
#endif
#ifndef InternalSetup
  #define InternalSetup "0"
#endif
#define MyAppPublisher "Clinica PC"
#define MyAppExeName "KioskClinicaPC.exe"

; Carpeta con el resultado de `dotnet publish` (ruta relativa a este .iss).
#define PublishDir "..\publish"
#define AgentPublishDir "..\publish-agent"
#define MaintenancePublishDir "..\publish-maintenance"
#define SetupHelperPublishDir "..\publish-setup-helper"
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
#if InternalSetup == "1"
OutputBaseFilename=Setup-EquipoClinicaPC-{#MyAppVersion}
#else
OutputBaseFilename=Setup-KioskClinicaPC-{#MyAppVersion}
#endif
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
Uninstallable=ShouldInstallKiosk

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Types]
#if InternalSetup == "1"
Name: "full"; Description: "Kiosk y pack de aplicaciones"
Name: "kioskonly"; Description: "Solo Kiosk"
Name: "custom"; Description: "Personalizada"; Flags: iscustom
#else
Name: "kioskonly"; Description: "Kiosk"
#endif

[Components]
#if InternalSetup == "1"
Name: "kiosk"; Description: "Instalar Kiosko Clínica PC"; Types: full kioskonly
Name: "pack"; Description: "Instalar pack de aplicaciones del servidor"; Types: full
#else
Name: "kiosk"; Description: "Instalar Kiosko Clínica PC"; Types: kioskonly; Flags: fixed
#endif

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
#if InternalSetup == "1"
Source: "{#SetupHelperPublishDir}\KioskSetupHelper.exe"; Flags: dontcopy noencryption
#endif

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
; Lanza la app al terminar -> dispara el auto-registro de arranque (HKCU\...\Run) de la propia app.
; skipifsilent: en un upgrade silencioso (tarea SYSTEM) NO se relanza aqui (seria sesion 0); el
; reinicio del updater.cmd + autostart lo trae de vuelta en la sesion del usuario.
Filename: "{app}\{#MyAppExeName}"; Parameters: "--register-autostart-only"; Flags: runhidden runasoriginaluser; Components: kiosk; Check: ShouldRegisterOnly
Filename: "{sys}\shutdown.exe"; Parameters: "/r /t 60 /c ""Las aplicaciones se han instalado. El equipo se reiniciará en 60 segundos."""; Flags: runhidden; Check: ShouldScheduleRestart
Filename: "{app}\{#MyAppExeName}"; Description: "Ejecutar {#MyAppName} ahora"; Flags: nowait postinstall skipifsilent runasoriginaluser; Components: kiosk; Check: ShouldLaunchKiosk

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
  ProvisionedInitialSetupKey = '{#DefaultInitialSetupKey}';

var
  ServerProvisioningWritten: Boolean;
  PackNeedsRestart: Boolean;
  PackSummary: String;
  RestartCancelled: Boolean;
#if InternalSetup == "1"
  AppsPage: TInputOptionWizardPage;
  PackageIds: array of String;
  CatalogLoaded: Boolean;
  CancelRestartButton: TNewButton;
#endif

function ShouldInstallKiosk(): Boolean;
begin
  Result := WizardIsComponentSelected('kiosk');
end;

function ShouldInstallPack(): Boolean;
begin
#if InternalSetup == "1"
  Result := WizardIsComponentSelected('pack');
#else
  Result := False;
#endif
end;

function ShouldRegisterOnly(): Boolean;
begin
  Result := ShouldInstallKiosk() and PackNeedsRestart and (not WizardSilent);
end;

function ShouldScheduleRestart(): Boolean;
begin
  Result := PackNeedsRestart and (not WizardSilent);
end;

function ShouldLaunchKiosk(): Boolean;
begin
  Result := ShouldInstallKiosk() and (not PackNeedsRestart);
end;

function JsonEscape(const Value: String): String;
begin
  Result := Value;
  StringChangeEx(Result, '\', '\\', True);
  StringChangeEx(Result, '"', '\"', True);
  StringChangeEx(Result, #13, '\r', True);
  StringChangeEx(Result, #10, '\n', True);
end;

function NormalizedServerUrl(): String; forward;

#if InternalSetup == "1"
function SetupRequestText(): String;
begin
  Result := '[Setup]' + #13#10 +
    'ServerUrl=' + NormalizedServerUrl() + #13#10 +
    'SetupKey=' + ProvisionedInitialSetupKey + #13#10 +
    'Version={#MyAppVersion}' + #13#10;
end;

function LoadSetupCatalog(var Detail: String): Boolean;
var
  RequestPath, ResultPath, HelperPath, Name, LabelText: String;
  ResultCode, Count, I: Integer;
  SizeBytes: Int64;
  IsDefault, WasApplied: Boolean;
begin
  Result := False;
  Detail := '';
  if (NormalizedServerUrl() = '') or (ProvisionedInitialSetupKey = '') then
  begin
    Detail := 'Este instalador no contiene la configuración necesaria para descargar el pack.';
    exit;
  end;

  try
    ExtractTemporaryFile('KioskSetupHelper.exe');
    RequestPath := ExpandConstant('{tmp}\setup-catalog-request.ini');
    ResultPath := ExpandConstant('{tmp}\setup-catalog-result.ini');
    HelperPath := ExpandConstant('{tmp}\KioskSetupHelper.exe');
    DeleteFile(ResultPath);
    if not SaveStringToFile(RequestPath, SetupRequestText(), False) then
      RaiseException('No se pudo preparar la consulta del catálogo.');
    if not Exec(HelperPath, 'catalog "' + RequestPath + '" "' + ResultPath + '"', ExpandConstant('{tmp}'),
      SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      RaiseException('No se pudo ejecutar el asistente del pack.');
    if GetIniString('Result', 'Ok', '0', ResultPath) <> '1' then
      RaiseException(GetIniString('Result', 'Error', 'No se pudo leer el catálogo.', ResultPath));

    Count := StrToIntDef(GetIniString('Result', 'Count', '0', ResultPath), 0);
    if Count = 0 then RaiseException('El servidor no tiene aplicaciones habilitadas para el Setup.');
    AppsPage.CheckListBox.Items.Clear;
    SetArrayLength(PackageIds, Count);
    for I := 0 to Count - 1 do
    begin
      PackageIds[I] := GetIniString('Package' + IntToStr(I), 'Id', '', ResultPath);
      Name := GetIniString('Package' + IntToStr(I), 'Name', 'Aplicación', ResultPath);
      SizeBytes := StrToInt64Def(GetIniString('Package' + IntToStr(I), 'SizeBytes', '0', ResultPath), 0);
      IsDefault := GetIniString('Package' + IntToStr(I), 'Default', '0', ResultPath) = '1';
      WasApplied := GetIniString('Package' + IntToStr(I), 'Applied', '0', ResultPath) = '1';
      LabelText := Name + '  (' + IntToStr(SizeBytes div 1024 div 1024) + ' MB)';
      if WasApplied then LabelText := LabelText + '  — aplicada anteriormente';
      AppsPage.Add(LabelText);
      AppsPage.Values[I] := IsDefault and (not WasApplied);
    end;
    CatalogLoaded := True;
    Result := True;
  except
    Detail := GetExceptionMessage();
  end;
end;

function SelectedPackageCount(): Integer;
var I: Integer;
begin
  Result := 0;
  for I := 0 to GetArrayLength(PackageIds) - 1 do
    if AppsPage.Values[I] then Result := Result + 1;
end;

procedure InstallSelectedPackages();
var
  RequestPath, ResultPath, HelperPath, RequestText: String;
  ResultCode, I, SelectedIndex: Integer;
begin
  if (not ShouldInstallPack()) or (SelectedPackageCount() = 0) then exit;
  RequestText := SetupRequestText() + #13#10 + '[Selection]' + #13#10 +
    'Count=' + IntToStr(SelectedPackageCount()) + #13#10;
  SelectedIndex := 0;
  for I := 0 to GetArrayLength(PackageIds) - 1 do
    if AppsPage.Values[I] then
    begin
      RequestText := RequestText + 'Id' + IntToStr(SelectedIndex) + '=' + PackageIds[I] + #13#10;
      SelectedIndex := SelectedIndex + 1;
    end;

  RequestPath := ExpandConstant('{tmp}\setup-install-request.ini');
  ResultPath := ExpandConstant('{tmp}\setup-install-result.ini');
  HelperPath := ExpandConstant('{tmp}\KioskSetupHelper.exe');
  DeleteFile(ResultPath);
  SaveStringToFile(RequestPath, RequestText, False);
  WizardForm.StatusLabel.Caption := 'Descargando e instalando las aplicaciones seleccionadas…';
  if not Exec(HelperPath, 'install "' + RequestPath + '" "' + ResultPath + '"', ExpandConstant('{tmp}'),
    SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    PackSummary := 'No se pudo ejecutar el instalador de aplicaciones.';
    exit;
  end;
  PackNeedsRestart := GetIniString('Result', 'RebootRequired', '0', ResultPath) = '1';
  PackSummary := GetIniString('Result', 'Summary', '', ResultPath);
  StringChangeEx(PackSummary, '\n', #13#10, True);
  if PackSummary = '' then
    PackSummary := GetIniString('Result', 'Error', 'No se recibió un resultado del pack.', ResultPath);
end;

procedure CancelScheduledRestart(Sender: TObject);
var ResultCode: Integer;
begin
  if Exec(ExpandConstant('{sys}\shutdown.exe'), '/a', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    RestartCancelled := True;
    CancelRestartButton.Visible := False;
    WizardForm.FinishedLabel.Caption := 'El reinicio automático se ha cancelado.' + #13#10 + PackSummary;
    if ShouldInstallKiosk() then
      ExecAsOriginalUser(ExpandConstant('{app}\{#MyAppExeName}'), '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
  end;
end;

procedure InitializeWizard();
begin
  AppsPage := CreateInputOptionPage(wpSelectComponents,
    'Aplicaciones del pack', 'Selecciona las aplicaciones que quieres instalar',
    'Las aplicaciones se descargarán del servidor, se verificarán y se instalarán sin más intervención.', True, False);
  CancelRestartButton := TNewButton.Create(WizardForm);
  CancelRestartButton.Parent := WizardForm.FinishedPage;
  CancelRestartButton.Caption := 'Cancelar reinicio automático';
  CancelRestartButton.Left := WizardForm.FinishedLabel.Left;
  CancelRestartButton.Top := WizardForm.FinishedLabel.Top + WizardForm.FinishedLabel.Height + ScaleY(24);
  CancelRestartButton.Width := ScaleX(210);
  CancelRestartButton.OnClick := @CancelScheduledRestart;
  CancelRestartButton.Visible := False;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = AppsPage.ID) and (not ShouldInstallPack());
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var Detail: String;
begin
  Result := True;
  if CurPageID = wpSelectComponents then
  begin
    if (not ShouldInstallKiosk()) and (not ShouldInstallPack()) then
    begin
      MsgBox('Selecciona Kiosk, el pack de aplicaciones o ambos.', mbError, MB_OK);
      Result := False;
      exit;
    end;
    if ShouldInstallPack() and (not CatalogLoaded) and (not LoadSetupCatalog(Detail)) then
    begin
      if ShouldInstallKiosk() and
        (MsgBox(Detail + #13#10#13#10 + '¿Quieres continuar instalando solo Kiosk?', mbConfirmation, MB_YESNO) = IDYES) then
        WizardSelectComponents('kiosk')
      else
        Result := False;
    end;
  end
  else if (CurPageID = AppsPage.ID) and (SelectedPackageCount() = 0) then
  begin
    MsgBox('Selecciona al menos una aplicación o vuelve atrás y desmarca el pack.', mbError, MB_OK);
    Result := False;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then
  begin
    if PackSummary <> '' then
      WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + #13#10#13#10 + PackSummary;
    if PackNeedsRestart then
    begin
      WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + #13#10#13#10 +
        'El equipo se reiniciará automáticamente en 60 segundos.';
      CancelRestartButton.Visible := True;
    end;
  end;
end;
#endif

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
    MsgBox('El servidor responde. El kiosco aplicará estos datos al iniciarse si aún no tiene servidor configurado.' + #13#10 +
      NormalizedServerUrl(), mbInformation, MB_OK)
  else
    MsgBox('El instalador incluye los datos del servidor, pero no ha podido verificar la conexión.' + #13#10#13#10 +
      Detail + #13#10#13#10 +
      'La instalación continuará y el kiosco volverá a intentarlo automáticamente al iniciarse.',
      mbError, MB_OK);
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
      NotifyServerConnection();
    end;
#if InternalSetup == "1"
    InstallSelectedPackages();
#endif
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
