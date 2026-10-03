#define AppVersion GetEnv("MAPANOTATKI_VERSION")
#define PublishDir GetEnv("MAPANOTATKI_PUBLISH_DIR")
#define RuntimeId GetEnv("MAPANOTATKI_RID")
#define AllowedArchitecture GetEnv("MAPANOTATKI_ALLOWED_ARCH")

[Setup]
AppId={{A6B7B8A4-2F15-4A60-9CF5-1C7791B909E4}
AppName=MapaNotatek
AppVersion={#AppVersion}
AppVerName=MapaNotatek {#AppVersion}
AppPublisher=MapaNotatek
DefaultDirName={localappdata}\Programs\MapaNotatek
DefaultGroupName=MapaNotatek
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed={#AllowedArchitecture}
ArchitecturesInstallIn64BitMode={#AllowedArchitecture}
OutputDir=..\..\artifacts
OutputBaseFilename=MapaNotatek-Setup-{#RuntimeId}
SetupIconFile=..\..\src\MapaNotatek\Assets\AppIcon.ico
UninstallDisplayIcon={app}\MapaNotatek.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Tasks]
Name: "desktopicon"; Description: "Utwórz skrót na pulpicie"; GroupDescription: "Dodatkowe skróty:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\MapaNotatek"; Filename: "{app}\MapaNotatek.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\MapaNotatek"; Filename: "{app}\MapaNotatek.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\MapaNotatek.exe"; Description: "Uruchom MapaNotatek"; Flags: nowait postinstall skipifsilent
