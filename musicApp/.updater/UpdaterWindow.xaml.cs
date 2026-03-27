using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using musicApp.Helpers;

namespace musicApp.Updater;

public partial class UpdaterWindow : Window
{
    private readonly CancellationTokenSource _cts = new();

    private string? _installRoot;
    private RemoteVersionResult _remote;
    private bool _updateAvailable;
    private bool _postUpdateLaunchMode;
    private bool _upToDateDoneMode;
    private bool _postUpdateAutoCloseEligible;
    private VersionBuild _buildKind;
    private DispatcherTimer? _upToDateAutoCloseTimer;
    private int _upToDateAutoCloseSecondsLeft;

    private const int AutoInstallAutoCloseTotalSeconds = 5;
    private const int PostLaunchCloseTotalSeconds = 5;

    public UpdaterWindow()
    {
        InitializeComponent();
        try
        {
            Icon = BitmapFrame.Create(new Uri(
                "pack://application:,,,/musicApp-updater;component/Resources/icon/musicApp%20Icon.ico",
                UriKind.Absolute));
        }
        catch
        {
            // ignore
        }
        SourceInitialized += (_, _) => WindowsTitleBarTheme.ApplyImmersiveDarkMode(this);
        Loaded += UpdaterWindow_Loaded;
        Closing += (_, _) =>
        {
            StopUpToDateAutoCloseTimer();
            _cts.Cancel();
        };
        PreviewMouseDown += OnUpToDateAutoCloseUserInteraction;
        PreviewMouseWheel += OnUpToDateAutoCloseUserInteraction;
        PreviewKeyDown += OnUpToDateAutoCloseUserInteraction;
        PreviewTouchDown += OnUpToDateAutoCloseTouchDown;
    }

    private void StopUpToDateAutoCloseTimer()
    {
        if (_upToDateAutoCloseTimer != null)
        {
            _upToDateAutoCloseTimer.Stop();
            _upToDateAutoCloseTimer.Tick -= OnUpToDateAutoCloseTick;
            _upToDateAutoCloseTimer = null;
        }

        AutoCloseCountdownText.Visibility = Visibility.Collapsed;
        if (_postUpdateLaunchMode)
            UpdateButton.IsEnabled = true;
    }

    private void OnUpToDateAutoCloseTick(object? sender, EventArgs e)
    {
        _upToDateAutoCloseSecondsLeft--;
        if (_upToDateAutoCloseSecondsLeft <= 0)
        {
            StopUpToDateAutoCloseTimer();
            Close();
            return;
        }

        AutoCloseCountdownText.Text = $"Closing in {_upToDateAutoCloseSecondsLeft}s";
    }

    private void StartAutoInstallAutoCloseCountdownIfNeeded()
    {
        StopUpToDateAutoCloseTimer();
        if (AutoInstallCheckBox.IsChecked != true)
            return;
        if (!_upToDateDoneMode && !_postUpdateAutoCloseEligible)
            return;
        _upToDateAutoCloseSecondsLeft = AutoInstallAutoCloseTotalSeconds;
        AutoCloseCountdownText.Text = $"Closing in {_upToDateAutoCloseSecondsLeft}s";
        AutoCloseCountdownText.Visibility = Visibility.Visible;
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        t.Tick += OnUpToDateAutoCloseTick;
        _upToDateAutoCloseTimer = t;
        t.Start();
    }

    /// <summary>
    /// After musicApp is started (installer/portable path or Launch button), close the updater after a short delay.
    /// </summary>
    private void StartPostLaunchCloseCountdown()
    {
        StopUpToDateAutoCloseTimer();
        UpdateButton.IsEnabled = false;
        _upToDateAutoCloseSecondsLeft = PostLaunchCloseTotalSeconds;
        AutoCloseCountdownText.Text = $"Closing in {_upToDateAutoCloseSecondsLeft}s";
        AutoCloseCountdownText.Visibility = Visibility.Visible;
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        t.Tick += OnUpToDateAutoCloseTick;
        _upToDateAutoCloseTimer = t;
        t.Start();
    }

    private void OnUpToDateAutoCloseUserInteraction(object sender, RoutedEventArgs e)
    {
        if (_upToDateAutoCloseTimer != null)
            StopUpToDateAutoCloseTimer();
    }

    private void OnUpToDateAutoCloseTouchDown(object? sender, TouchEventArgs e)
    {
        if (_upToDateAutoCloseTimer != null)
            StopUpToDateAutoCloseTimer();
    }

    private async void UpdaterWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            SetStatus("Reading installed version…");
            Progress.IsIndeterminate = true;

            var args = Environment.GetCommandLineArgs();
            _installRoot = InstallVersionReader.TryResolveInstallRoot(args.Length > 1 ? args[1] : null);
            if (string.IsNullOrEmpty(_installRoot))
            {
                SetDone("Could not find the install folder (no Version file next to this updater).");
                return;
            }

            var vbRaw = InstallVersionReader.TryReadVersionBuild(_installRoot);
            if (!VersionBuildExtensions.TryParseFromFileContent(vbRaw, out _buildKind))
            {
                SetDone($"Unknown VersionBuild value: '{vbRaw ?? "(missing)"}'. Expected portable, x64, or x86.");
                return;
            }

            var localTagFile = InstallVersionReader.TryReadVersionTag(_installRoot);
            var localRaw = InstallVersionReader.TryReadVersion(_installRoot);
            if (string.IsNullOrEmpty(localRaw))
            {
                SetDone("Could not find a Version file in the install folder.");
                return;
            }

            SetStatus("Fetching releases from GitHub…");
            var remote = await GitHubTagsVersionService.FetchLatestVersionTagAsync(_cts.Token);
            if (remote.Error != null)
            {
                SetDone(remote.Error);
                return;
            }

            if (remote.LatestVersion == null)
            {
                SetDone("Could not determine the latest version from GitHub releases.");
                return;
            }

            _remote = remote;

            static string FormatVersionLine(Version v, string? versionTag)
            {
                var line = $"v{v}";
                if (!string.IsNullOrWhiteSpace(versionTag))
                    line += " " + versionTag.Trim();
                return line;
            }

            static string FormatVersionLineWithOptionalDate(Version v, string? versionTag, DateTimeOffset? publishedAt)
            {
                var line = FormatVersionLine(v, versionTag);
                if (publishedAt is { } p)
                    line += " " + FormatReleaseDateParen(p);
                return line;
            }

            static string FormatReleaseDateParen(DateTimeOffset publishedAt) =>
                "(" + publishedAt.ToLocalTime().ToString("M/d/yy", CultureInfo.InvariantCulture) + ")";

            if (!VersionComparer.TryParse(localRaw, out var localVer))
            {
                SetDone($"Installed version could not be parsed: {localRaw}");
                return;
            }

            SetStatus("Fetching release dates…");
            DateTimeOffset? localPublishedAt = await GitHubTagsVersionService.TryFetchReleasePublishedAtForTagAsync(
                GitHubTagsVersionService.TagNameFromVersion(localVer),
                _cts.Token);

            var localDisplay = FormatVersionLineWithOptionalDate(localVer, localTagFile, localPublishedAt);
            var remoteDisplay = FormatVersionLineWithOptionalDate(
                remote.LatestVersion,
                remote.LatestReleaseVersionTag,
                remote.PublishedAt);

            var cmp = remote.LatestVersion.CompareTo(localVer);
            Progress.IsIndeterminate = false;
            Progress.Value = 0;
            Progress.Maximum = 100;

            if (cmp > 0)
            {
                _upToDateDoneMode = false;
                _postUpdateAutoCloseEligible = false;
                _updateAvailable = true;
                StatusText.Text = $"Update available: {remoteDisplay}{Environment.NewLine}Installed: {localDisplay}";
                ActionRow.Visibility = Visibility.Visible;
                UpdateButton.Content = "Update now";
                UpdateButton.Visibility = Visibility.Visible;
                CancelButton.Visibility = Visibility.Visible;
                AutoInstallCheckBox.IsChecked = UpdaterPreferences.ReadAutomaticallyInstallUpdates();
                LaunchAfterUpdateCheckBox.IsChecked = UpdaterPreferences.ReadLaunchAppAfterUpdate();

                if (UpdaterPreferences.ReadAutomaticallyInstallUpdates())
                    await RunUpdateAsync();
            }
            else if (cmp < 0)
            {
                _upToDateDoneMode = false;
                _postUpdateAutoCloseEligible = false;
                CancelButton.Visibility = Visibility.Visible;
                SetDone($"Your version {localDisplay} is newer than the latest release on GitHub {remoteDisplay}.");
            }
            else
            {
                _upToDateDoneMode = true;
                _postUpdateAutoCloseEligible = false;
                _updateAvailable = false;
                _postUpdateLaunchMode = false;
                SetDone($"You are up to date: {localDisplay}.");
                ActionRow.Visibility = Visibility.Visible;
                UpdateButton.Content = "Done";
                UpdateButton.Visibility = Visibility.Visible;
                CancelButton.Visibility = Visibility.Collapsed;
                AutoInstallCheckBox.IsChecked = UpdaterPreferences.ReadAutomaticallyInstallUpdates();
                LaunchAfterUpdateCheckBox.IsChecked = UpdaterPreferences.ReadLaunchAppAfterUpdate();
                StartAutoInstallAutoCloseCountdownIfNeeded();
            }
        }
        catch (OperationCanceledException)
        {
            Close();
        }
        catch (Exception ex)
        {
            SetDone($"Error: {ex.Message}");
        }
    }

    private void AutoInstallCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdaterPreferences.WriteAutomaticallyInstallUpdates(AutoInstallCheckBox.IsChecked == true);
    }

    private void LaunchAfterUpdateCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdaterPreferences.WriteLaunchAppAfterUpdate(LaunchAfterUpdateCheckBox.IsChecked == true);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        Close();
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_upToDateDoneMode)
        {
            Close();
            return;
        }

        if (_postUpdateLaunchMode)
        {
            if (!string.IsNullOrEmpty(_installRoot))
                ApplyUpdateService.TryLaunchMusicAppFromInstallRoot(_installRoot);
            StartPostLaunchCloseCountdown();
            return;
        }

        await RunUpdateAsync();
    }

    private async Task RunUpdateAsync()
    {
        if (!_updateAvailable || string.IsNullOrEmpty(_installRoot) || _remote.LatestVersion == null)
            return;

        _postUpdateAutoCloseEligible = false;
        StopUpToDateAutoCloseTimer();

        var token = _cts.Token;
        var launchAfter = LaunchAfterUpdateCheckBox.IsChecked == true;

        UpdateButton.IsEnabled = false;
        AutoInstallCheckBox.IsEnabled = false;
        LaunchAfterUpdateCheckBox.IsEnabled = false;

        var workDir = Path.Combine(Path.GetTempPath(), "musicApp-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        using var http = ReleaseDownloadService.CreateHttpClient();
        var updateSucceeded = false;

        try
        {
            SetStatus("Preparing download…");
            Progress.IsIndeterminate = false;
            Progress.Value = 0;

            var tagName = !string.IsNullOrWhiteSpace(_remote.LatestTagName)
                ? _remote.LatestTagName!
                : $"v{_remote.LatestVersion}";

            var assetName = ReleaseDownloadService.ExpectedAssetName(
                _remote.LatestVersion,
                _remote.LatestReleaseVersionTag,
                _buildKind);

            var url = await ReleaseDownloadService.ResolveDownloadUrlAsync(
                http,
                tagName,
                assetName,
                _remote.LatestVersion,
                _buildKind,
                token);

            var ext = _buildKind == VersionBuild.Portable ? ".zip" : ".exe";
            var packagePath = Path.Combine(workDir, "package" + ext);

            SetStatus($"Downloading {assetName}…");
            var progress = new Progress<double>(p =>
            {
                Progress.Value = Math.Clamp(p * 100.0, 0, 100);
            });

            await ReleaseDownloadService.DownloadToFileAsync(http, url, packagePath, progress, token);

            token.ThrowIfCancellationRequested();

            SetStatus("Installing…");
            Progress.IsIndeterminate = true;
            CancelButton.IsEnabled = false;

            await Task.Run(() =>
            {
                if (_buildKind == VersionBuild.Portable)
                    ApplyUpdateService.ApplyPortableZip(_installRoot!, packagePath, workDir, launchAfter);
                else
                    ApplyUpdateService.ApplyInstaller(_installRoot!, packagePath, launchAfter);
            }, token);

            Progress.IsIndeterminate = false;
            Progress.Value = 100;
            SetDone(launchAfter
                ? $"Update installed ({_remote.LatestVersion}). musicApp was started if possible."
                : $"Update installed ({_remote.LatestVersion}). Launch musicApp from your install folder when ready.");
            updateSucceeded = true;
        }
        catch (OperationCanceledException)
        {
            SetDone("Update cancelled.");
        }
        catch (Exception ex)
        {
            SetDone($"Update failed: {ex.Message}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(workDir))
                    Directory.Delete(workDir, true);
            }
            catch
            {
                // ignore
            }

            CancelButton.IsEnabled = true;

            if (_updateAvailable)
            {
                UpdateButton.IsEnabled = true;
                AutoInstallCheckBox.IsEnabled = true;
                LaunchAfterUpdateCheckBox.IsEnabled = true;

                if (updateSucceeded)
                {
                    _postUpdateLaunchMode = true;
                    UpdateButton.Content = "Launch";
                    _postUpdateAutoCloseEligible = true;
                    if (launchAfter)
                        StartPostLaunchCloseCountdown();
                    else
                        StartAutoInstallAutoCloseCountdownIfNeeded();
                }
                else
                {
                    _postUpdateLaunchMode = false;
                    _postUpdateAutoCloseEligible = false;
                    if (!_upToDateDoneMode)
                        UpdateButton.Content = "Update now";
                }
            }
        }
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;
    }

    private void SetDone(string text)
    {
        Progress.IsIndeterminate = false;
        Progress.Value = 100;
        Progress.Maximum = 100;
        StatusText.Text = text;
    }
}
