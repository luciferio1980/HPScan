; Inno Setup 6 script. Compilar en Windows con ISCC.exe.
; No instala controladores de Canon.

#define MyAppName "Canon Scan Studio"
#define MyAppVersion "1.0.0"
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
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
LicenseFile=
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
function DotNetDesktopInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App\8.0.0') or
            RegKeyExists(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App');
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  if not DotNetDesktopInstalled then
  begin
    MsgBox('Canon Scan Studio necesita el runtime de .NET 8 Desktop (Windows Desktop Runtime x64).' + #13#10 +
           'Instálalo desde https://dotnet.microsoft.com/download/dotnet/8.0 e inicia de nuevo el instalador.' + #13#10#13#10 +
           'Este programa NO instala el controlador del Canon PIXMA TS5151. Descárgalo desde el sitio oficial de Canon (serie TS5100).',
           mbInformation, MB_OK);
  end
  else
  begin
    MsgBox('Recuerda: este instalador no incluye controladores de Canon. Si Windows no detecta el TS5151, instala el MP Driver oficial de la serie TS5100.', mbInformation, MB_OK);
  end;
end;
