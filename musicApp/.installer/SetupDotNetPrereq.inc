var
  DotNetDownloadPage: TDownloadWizardPage;
  DotNetInstallPage: TOutputMarqueeProgressWizardPage;

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

procedure DotNetPrereqApplyGaugeColors;
begin
  if (WizardForm <> nil) and (WizardForm.ProgressGauge.Handle <> 0) then
  begin
    SendMessage(WizardForm.ProgressGauge.Handle, PBM_SETBKCOLOR, 0, ProgressBarBgColor);
    SendMessage(WizardForm.ProgressGauge.Handle, PBM_SETBARCOLOR, 0, ProgressBarFillColor);
  end;
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
