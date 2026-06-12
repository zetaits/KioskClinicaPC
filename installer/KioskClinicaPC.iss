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
; Solo PCs x64.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"

[Files]
; Copia TODO el contenido del publish (exe, dlls, runtime, Assets\Brands, Assets\SpecImages...).
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Desinstalar {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Lanza la app al terminar -> dispara el auto-registro de arranque (HKCU\...\Run) de la propia app.
Filename: "{app}\{#MyAppExeName}"; Description: "Ejecutar {#MyAppName} ahora"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Limpia la carpeta de instalacion. La config del usuario en %LOCALAPPDATA%\KioskClinicaPC NO se toca.
Type: filesandordirs; Name: "{app}"
