#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif
#define AppName "MAX Desktop"
#define AppExeName "MaxDesktop.App.exe"
#define AppId "{{8C0BBD31-2C2B-48CA-A5EE-B6DF1114B54F}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=lvlaksim1
AppPublisherURL=https://github.com/lvlaksim1/max-desktop
AppSupportURL=https://github.com/lvlaksim1/max-desktop/issues
DefaultDirName={localappdata}\Programs\MaxDesktop
DefaultGroupName=MAX Desktop
DisableDirPage=yes
DisableProgramGroupPage=yes
DirExistsWarning=no
UsePreviousAppDir=no
OutputDir={#OutputDir}
OutputBaseFilename=MaxDesktop_Setup_v{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\MaxDesktop.App\app.ico
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}
CloseApplications=yes
RestartApplications=no
SetupLogging=no
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription=MAX Desktop full installer

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\MAX Desktop"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{userdesktop}\MAX Desktop"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\MaxDesktop"; Flags: uninsdeletekey

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\MaxDesktop"
Type: filesandordirs; Name: "{userappdata}\MaxDesktop"
Type: files; Name: "{autoprograms}\MAX Desktop.lnk"
Type: files; Name: "{userdesktop}\MAX Desktop.lnk"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Запустить MAX Desktop"; Flags: nowait postinstall skipifsilent
