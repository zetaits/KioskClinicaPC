#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#ifndef DeploymentPublishDir
  #error DeploymentPublishDir is required
#endif
[Setup]
AppId={{C3426C20-8421-4DFB-A911-19F4E65E82FC}
AppName=ClínicaPC · Instalación por red
AppVersion={#MyAppVersion}
AppPublisher=ClínicaPC
DefaultDirName={autopf}\ClinicaPCDeployment
DefaultGroupName=ClínicaPC
OutputDir=Output
OutputBaseFilename=Setup-InstalacionRedClinicaPC-{#MyAppVersion}
SetupIconFile=..\src\Kiosk.Client\Assets\clinicapc-logo.ico
PrivilegesRequired=admin
MinVersion=10.0.22000
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartIfNeededByRun=no
[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
[Files]
Source: "{#DeploymentPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Instalación por red"; Filename: "{app}\manager\Kiosk.DeploymentManager.exe"
[Run]
Filename: "{sys}\sc.exe"; Parameters: "create ClinicaPCDeployment binPath= ""{app}\service\Kiosk.DeploymentService.exe"" start= auto DisplayName= ""ClínicaPC · Instalación por red"""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "failure ClinicaPCDeployment reset= 86400 actions= restart/10000/restart/30000/restart/60000"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "start ClinicaPCDeployment"; Flags: runhidden waituntilterminated
Filename: "{app}\manager\Kiosk.DeploymentManager.exe"; Description: "Abrir Instalación por red"; Flags: postinstall nowait skipifsilent runasoriginaluser
[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop ClinicaPCDeployment"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "delete ClinicaPCDeployment"; Flags: runhidden waituntilterminated
; Never remove images, DPAPI state, account passwords or job history on uninstall.
[Code]
var OperatorPage: TInputQueryWizardPage;
procedure InitializeWizard();
begin
  OperatorPage := CreateInputQueryPage(wpSelectDir, 'Encargado autorizado', 'Cuenta de Windows que abrirá la aplicación', 'Indica la cuenta local o DOMINIO\usuario del encargado. Los administradores también podrán abrirla.');
  OperatorPage.Add('Cuenta Windows:', False);
  OperatorPage.Values[0] := GetUserNameString();
end;
function CanMaintain(): Boolean;
var Code: Integer;
begin
  Result := True;
  if FileExists(ExpandConstant('{app}\service\Kiosk.DeploymentService.exe')) then
    Result := Exec(ExpandConstant('{app}\service\Kiosk.DeploymentService.exe'), '--prepare-maintenance', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
end;
function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer;
begin
  Result := '';
  if not CanMaintain() then begin Result := 'Hay instalaciones activas o resultados inciertos. Revísalos antes de actualizar.'; exit; end;
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop ClinicaPCDeployment', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;
function InitializeUninstall(): Boolean;
begin
  Result := CanMaintain();
  if not Result then MsgBox('Hay instalaciones activas o resultados inciertos. Revísalos antes de desinstalar.', mbError, MB_OK);
end;
procedure CurStepChanged(CurStep: TSetupStep);
var Code: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    if Pos('"', OperatorPage.Values[0]) > 0 then RaiseException('Cuenta Windows no válida.');
    if not Exec(ExpandConstant('{app}\service\Kiosk.DeploymentService.exe'), '--initialize-user "' + OperatorPage.Values[0] + '"', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      RaiseException('No se pudo autorizar al encargado. Comprueba la cuenta Windows y vuelve a ejecutar el instalador.');
  end;
end;
