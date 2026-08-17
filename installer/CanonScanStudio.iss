; Inno Setup 6 script. Compilar en Windows con ISCC.exe.
; No instala controladores de Canon.

#define MyAppName "Canon Scan Studio"
#define MyAppVersion "1.0.2"
#define MyAppPublisher "Canon Scan Studio"
#define MyAppExeName "CanonScanStudio.exe"

[Setup]
AppId={{8F2E1C3A-4B5D-4E6F-90A1-7C3B91A2D015}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\CanonScanStudio
DefaultGroupName={#MyAppName}
OutputDir=Output
OutputBaseFilename=CanonScanStudio-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
InfoAfterFile=..\dist\POSTINSTALL.txt
SetupLogging=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"

[Files]
Source: "..\artifacts\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Iniciar Canon Scan Studio"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup: Boolean;
begin
  Result := True;
end;
