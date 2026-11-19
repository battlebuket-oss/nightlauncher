#ifndef MyAppVersion
  #define MyAppVersion "1.2.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif

[Setup]
AppId={{3B8A4C19-4A95-4D19-9EB4-2DC88C6C91F0}
AppName=NightLauncher
AppVersion={#MyAppVersion}
AppPublisher=NightLauncher Contributors
AppPublisherURL=https://github.com/battlebuket-oss/nightlauncher
AppSupportURL=https://github.com/battlebuket-oss/nightlauncher/issues
AppUpdatesURL=https://github.com/battlebuket-oss/nightlauncher/releases
DefaultDirName={localappdata}\Programs\NightLauncher
DefaultGroupName=NightLauncher
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayName=NightLauncher
UninstallDisplayIcon={app}\NightLauncher.exe
OutputDir=..\artifacts
OutputBaseFilename=NightLauncher-Setup-{#MyAppVersion}
SetupLogging=yes
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
Uninstallable=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\NightLauncher"; Filename: "{app}\NightLauncher.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\NightLauncher"; Filename: "{app}\NightLauncher.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\NightLauncher.exe"; Description: "Launch NightLauncher"; WorkingDir: "{app}"; Flags: postinstall nowait skipifsilent
