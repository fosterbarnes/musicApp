#define AppName "musicApp"
#define AppDisplayName "musicApp (x86)"
#ifndef AppVersion
#define AppVersion "0.1.2"
#endif
#ifndef AppVersionTag
#define AppVersionTag "frederickRats"
#endif
#define AppPublisher "fosterbarnes"
#define AppURL "https://github.com/fosterbarnes/musicApp"
#define ExeName "musicApp.exe"

; WPF -> Windows Desktop bundle, not core-only dotnet-runtime-*.
#define DotNetPrereqUrl "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/8.0.25/windowsdesktop-runtime-8.0.25-win-x86.exe"
#define DotNetPrereqFile "windowsdesktop-runtime-8.0.25-win-x86.exe"
#define DotNetPrereqVersion "8.0.25"
#define DotNetPrereqRegArch "x86"

[Setup]
AppId={{114D67E2-45A7-4EC9-9A07-C33BD16FC619}
AppName={#AppName}
UninstallDisplayName={#AppDisplayName}
AppVersion={#AppVersion}
DisableDirPage=auto
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={localappdata}\{#AppName}
UninstallDisplayIcon={app}\{#ExeName}
ArchitecturesAllowed=x86compatible
ArchitecturesInstallIn64BitMode=
DisableProgramGroupPage=yes
LicenseFile=C:\Users\Foster\Documents\GitHub\musicApp\LICENSE
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
OutputBaseFilename=musicApp-x86-installer
SetupIconFile=..\..\.resources\icon\musicApp Icon.ico
SolidCompression=yes
WizardStyle=classic dark

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
SetupWindowTitle=musicApp v{#AppVersion} ({#AppVersionTag}) installer

[CustomMessages]
CreateStartMenuIcon=Create Start Menu shortcut

[Tasks]
Name: "desktopicon"; Description: "Create Desktop shortcut"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startmenuicon"; Description: "{cm:CreateStartMenuIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "dotnetdesktop8"; Description: ".NET 8.0 Desktop Runtime"; GroupDescription: "Install Dependencies"

[Files]
Source: "C:\Users\Foster\Documents\GitHub\musicApp\musicApp\bin\x86\Release\net8.0-windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#ExeName}"; Tasks: startmenuicon
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#ExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#ExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  PBM_SETBKCOLOR  = $0408;
  PBM_SETBARCOLOR = $0409;
  ProgressBarFillColor = $995370;
  ProgressBarBgColor = $2D2D2D;

#include "SetupDotNetPrereq.inc"

procedure InitializeWizard();
begin
  InitDotNetPrereqPages;
  WizardForm.LicenseAcceptedRadio.Checked := True;
  DotNetPrereqDeselectRuntimeTaskIfSatisfied;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  DotNetPrereqOnCurPageChanged(CurPageID);
end;

