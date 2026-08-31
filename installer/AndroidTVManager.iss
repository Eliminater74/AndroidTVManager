#ifndef AppVersion
  #define AppVersion "1.0.0-B4"
#endif

#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif

[Setup]
AppId={{B0D0E57C-3B54-4A1B-9B99-6D3B08B7EA74}
AppName=Android TV Manager
AppVersion={#AppVersion}
AppPublisher=Eliminater74
AppPublisherURL=https://github.com/Eliminater74/AndroidTVManager
AppSupportURL=https://github.com/Eliminater74/AndroidTVManager/issues
AppUpdatesURL=https://github.com/Eliminater74/AndroidTVManager/releases
DefaultDirName={localappdata}\Programs\Android TV Manager
DefaultGroupName=Android TV Manager
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
OutputDir=..\artifacts\release
OutputBaseFilename=AndroidTVManager-{#AppVersion}-Setup
SetupIconFile=..\src\AndroidTVManager.App\Assets\AndroidTVManager.ico
UninstallDisplayIcon={app}\AndroidTVManager.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Android TV Manager"; Filename: "{app}\AndroidTVManager.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Android TV Manager"; Filename: "{app}\AndroidTVManager.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\AndroidTVManager.exe"; Description: "Launch Android TV Manager"; Flags: nowait postinstall skipifsilent
