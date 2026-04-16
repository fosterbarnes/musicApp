#define AppName "musicApp"
#define AppDisplayName "musicApp (portable)"
#ifndef AppVersion
#define AppVersion "0.1.2"
#endif
#ifndef AppVersionTag
#define AppVersionTag "frederickRats"
#endif
#define AppPublisher "fosterbarnes"
#define AppURL "https://github.com/fosterbarnes/musicApp"
#define ExeName "musicApp.exe"

#define DotNetPrereqMultiArch
#define DotNetPrereqVersion "8.0.25"
; WPF -> Windows Desktop bundle, not core-only dotnet-runtime-*.
#define DotNetPrereqUrlX86 "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/8.0.25/windowsdesktop-runtime-8.0.25-win-x86.exe"
#define DotNetPrereqFileX86 "windowsdesktop-runtime-8.0.25-win-x86.exe"
#define DotNetPrereqUrlX64 "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/8.0.25/windowsdesktop-runtime-8.0.25-win-x64.exe"
#define DotNetPrereqFileX64 "windowsdesktop-runtime-8.0.25-win-x64.exe"
#define DotNetPrereqUrlArm64 "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/8.0.25/windowsdesktop-runtime-8.0.25-win-arm64.exe"
#define DotNetPrereqFileArm64 "windowsdesktop-runtime-8.0.25-win-arm64.exe"

[Setup]
AppId={{F1A2B3C4-D5E6-4F7A-8B9C-0D1E2F3A4B5C}
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
ArchitecturesInstallIn64BitMode=win64
DisableProgramGroupPage=yes
LicenseFile=C:\Users\Foster\Documents\GitHub\musicApp\LICENSE
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
OutputBaseFilename=musicApp-portable-installer
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
Source: "C:\Users\Foster\Documents\GitHub\musicApp\musicApp\bin\portable\Release\net8.0-windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

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
