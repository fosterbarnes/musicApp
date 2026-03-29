var
  DotNetDownloadPage: TDownloadWizardPage;
  DotNetInstallPage: TOutputMarqueeProgressWizardPage;
  DotNetDesktopRuntimeDetectDone: Boolean;
  DotNetDesktopRuntimeDetectHaveMin: Boolean;

function LeadingInt(const S: String): Integer;
var
  I: Integer;
  N: String;
begin
  N := '';
  for I := 1 to Length(S) do
  begin
    if (S[I] >= '0') and (S[I] <= '9') then
      N := N + S[I]
    else
      Break;
  end;
  if N = '' then
    Result := 0
  else
    Result := StrToIntDef(N, 0);
end;

function TakeFirstSegment(var S: String): Integer;
var
  P: Integer;
  Part: String;
begin
  P := Pos('.', S);
  if P > 0 then
  begin
    Part := Copy(S, 1, P - 1);
    Delete(S, 1, P);
  end
  else
  begin
    Part := S;
    S := '';
  end;
  Result := LeadingInt(Part);
end;

function DotNetVersionAtLeast(const InstalledVer, MinVer: String): Boolean;
var
  IA, IB: Integer;
  SA, SB: String;
begin
  SA := InstalledVer;
  SB := MinVer;
  repeat
    IA := TakeFirstSegment(SA);
    IB := TakeFirstSegment(SB);
    if IA > IB then
    begin
      Result := True;
      Exit;
    end;
    if IA < IB then
    begin
      Result := False;
      Exit;
    end;
  until (SA = '') and (SB = '');
  Result := True;
end;

function DotNetRegNamesMeetMin(
  const Names: TArrayOfString; const MinVer: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 0 to GetArrayLength(Names) - 1 do
    if (Names[I] <> '') and DotNetVersionAtLeast(Names[I], MinVer) then
    begin
      Result := True;
      Exit;
    end;
end;

function HasMinWindowsDesktopRuntimeScanKey(
  Root: Integer; const SubKey, MinVer: String): Boolean;
var
  Names: TArrayOfString;
begin
  Result := False;
  if (SubKey = '') or not RegKeyExists(Root, SubKey) then
    Exit;
  if RegGetSubkeyNames(Root, SubKey, Names) and
     DotNetRegNamesMeetMin(Names, MinVer) then
  begin
    Result := True;
    Exit;
  end;
  if RegGetValueNames(Root, SubKey, Names) then
    Result := DotNetRegNamesMeetMin(Names, MinVer);
end;

function HasMinWindowsDesktopRuntimeScanTree(
  Root: Integer; const BasePath, MinVer: String): Boolean;
begin
  Result := HasMinWindowsDesktopRuntimeScanKey(Root, BasePath, MinVer);
  if Result then
    Exit;
  Result :=
    HasMinWindowsDesktopRuntimeScanKey(
      Root, BasePath + '\InstalledVersions', MinVer);
end;

function HasMinWindowsDesktopRuntimeImpl: Boolean;
var
  MinVer: String;
  Paths: TArrayOfString;
  Roots: TArrayOfInteger;
  IPath, IRoot: Integer;
begin
  Result := False;
  MinVer := '{#DotNetPrereqVersion}';
  SetArrayLength(Paths, 2);
  Paths[0] :=
    'SOFTWARE\dotnet\Setup\InstalledVersions\{#DotNetPrereqRegArch}\sharedfx\Microsoft.WindowsDesktop.App';
  Paths[1] :=
    'SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\{#DotNetPrereqRegArch}\sharedfx\Microsoft.WindowsDesktop.App';
  if IsWin64 then
  begin
    SetArrayLength(Roots, 3);
    Roots[0] := HKEY_LOCAL_MACHINE;
    Roots[1] := HKLM64;
    Roots[2] := HKLM32;
  end
  else
  begin
    SetArrayLength(Roots, 1);
    Roots[0] := HKEY_LOCAL_MACHINE;
  end;
  for IRoot := 0 to GetArrayLength(Roots) - 1 do
    for IPath := 0 to GetArrayLength(Paths) - 1 do
      if HasMinWindowsDesktopRuntimeScanTree(Roots[IRoot], Paths[IPath], MinVer) then
      begin
        Result := True;
        Exit;
      end;
end;

function HasMinWindowsDesktopRuntime: Boolean;
begin
  if not DotNetDesktopRuntimeDetectDone then
  begin
    DotNetDesktopRuntimeDetectHaveMin := HasMinWindowsDesktopRuntimeImpl;
    DotNetDesktopRuntimeDetectDone := True;
  end;
  Result := DotNetDesktopRuntimeDetectHaveMin;
end;

procedure DotNetPrereqDeselectRuntimeTaskIfSatisfied;
begin
  if not HasMinWindowsDesktopRuntime then
    Exit;
  WizardSelectTasks('!dotnetdesktop8');
end;

procedure DotNetPrereqApplyGaugeColors;
begin
  if (WizardForm <> nil) and (WizardForm.ProgressGauge.Handle <> 0) then
  begin
    SendMessage(WizardForm.ProgressGauge.Handle, PBM_SETBKCOLOR, 0, ProgressBarBgColor);
    SendMessage(WizardForm.ProgressGauge.Handle, PBM_SETBARCOLOR, 0, ProgressBarFillColor);
  end;
end;

procedure DotNetPrereqOnCurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpSelectTasks) or (CurPageID = wpReady) then
    DotNetPrereqDeselectRuntimeTaskIfSatisfied
  else if CurPageID = wpInstalling then
    DotNetPrereqApplyGaugeColors;
end;

procedure InitDotNetPrereqPages;
begin
  DotNetDownloadPage := CreateDownloadPage(
    'Microsoft .NET 8 Desktop Runtime',
    'Downloading the official installer from Microsoft.',
    nil);
  DotNetDownloadPage.ShowBaseNameInsteadOfUrl := True;
  DotNetInstallPage := CreateOutputMarqueeProgressPage(
    'Microsoft .NET 8 Desktop Runtime',
    'Running the Microsoft installer — please wait.');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExePath: String;
  ExitCode: Integer;
  InstallParams: String;
begin
  Result := '';
  NeedsRestart := False;
  InstallParams := '/install /passive /norestart';

  if not WizardIsTaskSelected('dotnetdesktop8') then
    Exit;

  try
    if WizardSilent then
    begin
      DownloadTemporaryFile(
        '{#DotNetPrereqUrl}', '{#DotNetPrereqFile}', '', nil);
    end
    else
    begin
      DotNetDownloadPage.Show;
      try
        DotNetDownloadPage.Clear;
        DotNetDownloadPage.Add('{#DotNetPrereqUrl}', '{#DotNetPrereqFile}', '');
        DotNetDownloadPage.Download;
      finally
        DotNetDownloadPage.Hide;
      end;
    end;
    ExePath := ExpandConstant('{tmp}\{#DotNetPrereqFile}');
  except
    Result :=
      'Could not download the Microsoft .NET 8 Desktop Runtime installer.' + #13#10#13#10 +
      'Check your connection and try again, or install .NET 8 from https://dotnet.microsoft.com/download/dotnet/8.0';
    Exit;
  end;

  if not FileExists(ExePath) then
  begin
    Result := 'The downloaded .NET installer file is missing. Try running this setup again.';
    Exit;
  end;

  ExitCode := -1;
  if WizardSilent then
  begin
    if not Exec(ExePath, InstallParams, '', SW_SHOW, ewWaitUntilTerminated, ExitCode) then
      Result := 'Could not start the Microsoft .NET Desktop Runtime installer.';
  end
  else
  begin
    DotNetInstallPage.Show;
    try
      DotNetInstallPage.SetText(
        'Running Microsoft .NET Desktop Runtime {#DotNetPrereqVersion} setup.',
        '{#DotNetPrereqFile} — a separate Microsoft window may also appear.');
      DotNetInstallPage.Animate;
      if not Exec(ExePath, InstallParams, '', SW_SHOW, ewWaitUntilTerminated, ExitCode) then
        Result := 'Could not start the Microsoft .NET Desktop Runtime installer.';
    finally
      DotNetInstallPage.Hide;
    end;
  end;

  if Result <> '' then
    Exit;

  Log('musicApp prereq: Microsoft .NET Desktop installer exit code ' + IntToStr(ExitCode));

  NeedsRestart := (ExitCode = 3010) or (ExitCode = 1641);

  if (ExitCode = 0) or (ExitCode = 3010) or (ExitCode = 1641) or (ExitCode = 1638) then
    Exit;

  Result :=
    'Microsoft .NET Desktop Runtime setup exited with code ' + IntToStr(ExitCode) + '.' + #13#10#13#10 +
    'Install .NET 8 from https://dotnet.microsoft.com/download/dotnet/8.0, then run this setup again.';
end;
