; Report Expert — Inno Setup script
; Build: run scripts/build-release.ps1 from the repo root (packages publish output + optional zip + installer)

#ifndef MyAppVersion
  #define MyAppVersion "2.0.0"
#endif

#ifndef MyAppDisplayVersion
  #define MyAppDisplayVersion "2.0"
#endif

#ifndef MyAppCodename
  #define MyAppCodename "Baboon"
#endif

#define MyAppName "Report Expert"
#define MyAppPublisher "N1ERP"
#define MyAppExeName "ReportExpert.exe"
#define MyAppURL "https://apps.microsoft.com/detail/9nwqjz08pqzd"
#define MyAppSupportURL "https://github.com/n1erp/bc-report-expert/issues"
#define MyAppUpdatesURL "https://apps.microsoft.com/detail/9nwqjz08pqzd"
#define PublishDir "..\artifacts\publish\win-x64"
#define MyAppIcon "..\Assets\app-icon.ico"

[Setup]
AppId={{8843113A-B79F-43BA-8E36-8D585EAE3B7B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppDisplayVersion} {#MyAppCodename}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppSupportURL}
AppUpdatesURL={#MyAppUpdatesURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=ReportExpert-Setup-v{#MyAppDisplayVersion}-{#MyAppCodename}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile={#MyAppIcon}
UninstallDisplayIcon={app}\{#MyAppExeName}
DisableProgramGroupPage=yes
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Messages]
WelcomeLabel2=This will install [name/ver] on your computer.%n%nReport Expert is a desktop toolkit for RDLC report development — preview, Copilot edits, and Business Central report gallery.
