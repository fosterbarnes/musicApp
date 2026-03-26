#define MyAppName "musicApp"
#define MyAppVersion "0.0.18"
#define MyAppPublisher "fosterbarnes"
#define MyAppURL "https://github.com/fosterbarnes/musicApp"
#define MyAppExeName "musicApp.exe"

[Setup]
AppId={{114D67E2-45A7-4EC9-9A07-C33BD16FC619}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\{#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x86compatible
ArchitecturesInstallIn64BitMode=
DisableProgramGroupPage=yes
LicenseFile=C:\Users\Foster\Documents\GitHub\musicApp\LICENSE
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
OutputBaseFilename=musicApp-x86-installer
SetupIconFile=C:\Users\Foster\Documents\GitHub\musicApp\musicApp\Resources\icon\musicApp Icon.ico
SolidCompression=yes
WizardStyle=classic dark

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
CreateStartMenuIcon=Create Start Menu shortcut

[Tasks]
Name: "desktopicon"; Description: "Create Desktop shortcut"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startmenuicon"; Description: "{cm:CreateStartMenuIcon}"; GroupDescription: "{cm:AdditionalIcons}"
[Files]
Source: "C:\Users\Foster\Documents\GitHub\musicApp\musicApp\bin\x86\Release\net8.0-windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenuicon
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  PBM_SETBKCOLOR  = $0408; // background (unfilled portion) - COLORREF, BGR hex
  PBM_SETBARCOLOR = $0409; // bar/filled portion - COLORREF, BGR hex

  // fill: 705399 (interpreted as hex RGB) => BGR = 995370
  ProgressBarFillColor = $995370;
  // bg: picked to match your installer theme
  ProgressBarBgColor = $2D2D2D;

procedure InitializeWizard();
begin
  WizardForm.LicenseAcceptedRadio.Checked := True;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpInstalling then
  begin
    // Apply colors using WinAPI messages for the gauge control.
    SendMessage(WizardForm.ProgressGauge.Handle, PBM_SETBKCOLOR, 0, ProgressBarBgColor);
    SendMessage(WizardForm.ProgressGauge.Handle, PBM_SETBARCOLOR, 0, ProgressBarFillColor);
  end;
end;

