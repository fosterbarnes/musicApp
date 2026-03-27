using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using musicApp;
using musicApp.Dialogs;
using musicApp.Helpers;

namespace musicApp.Views
{
    public partial class SettingsView : Window
    {
        private static readonly string[] SettingsSectionOrder =
        {
            "General", "Playback", "Library", "KeyboardShortcuts", "Theme", "About"
        };

        private PreferencesManager.AppPreferences _preferences = PreferencesManager.CreateDefaultPreferences();
        private string _aboutInstallPath = "";
        private string _aboutSettingsPath = "";
        private string _fileStorageMusicPathOpen = "";
        private bool _playbackNormalizationCheckLoading;
        private bool _sidebarLibraryActionCheckLoading;
        private bool _libraryAlbumArtScanGuiBusy;

        public SettingsView(string? launchSection = null)
        {
            InitializeComponent();
            SourceInitialized += (_, _) => WindowsTitleBarTheme.ApplyImmersiveDarkMode(this);
            KeyboardShortcutsInAppListView.ItemsSource = KeyboardShortcutCatalog.InApp;
            KeyboardShortcutsGlobalListView.ItemsSource = KeyboardShortcutCatalog.Global;
            KeyboardShortcutsInAppListView.SizeChanged += (_, _) => BalanceKeyboardShortcutColumns(KeyboardShortcutsInAppListView);
            KeyboardShortcutsGlobalListView.SizeChanged += (_, _) => BalanceKeyboardShortcutColumns(KeyboardShortcutsGlobalListView);
            Loaded += SettingsView_Loaded;
            ShowSection(launchSection ?? "General");
        }

        protected override void OnClosed(EventArgs e)
        {
            CleanupLibraryAlbumArtScanGuiState();
            base.OnClosed(e);
            WindowFocusHelper.ScheduleActivateOwner(this);
        }

        private void SettingsView_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyAboutTitle();
            ApplyAboutVersionSubtitle();
            BalanceKeyboardShortcutColumns(KeyboardShortcutsInAppListView);
            BalanceKeyboardShortcutColumns(KeyboardShortcutsGlobalListView);

            try
            {
                var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                _aboutInstallPath = string.IsNullOrEmpty(baseDir) ? "-" : baseDir;
            }
            catch
            {
                _aboutInstallPath = "-";
            }

            SetAboutPathRichText(AboutInstallLocationText, _aboutInstallPath);
            AboutOpenInstallLocationButton.IsEnabled = CanOpenAboutFolder(_aboutInstallPath);

            try
            {
                _aboutSettingsPath = LibraryManager.SettingsDirectoryPath;
            }
            catch
            {
                _aboutSettingsPath = "-";
            }

            SetAboutPathRichText(AboutSettingsLocationText, _aboutSettingsPath);
            AboutOpenSettingsLocationButton.IsEnabled = CanOpenAboutFolder(_aboutSettingsPath);

            FileStorageSettingsPathText.Text = _aboutSettingsPath ?? "";
            FileStorageOpenSettingsPathButton.IsEnabled = CanOpenAboutFolder(_aboutSettingsPath);

            _ = LoadFileStorageMusicPathsAsync();

            AppLanguageCatalog.PopulateGeneralLanguageComboBox(GeneralLanguageComboBox);
            LoadPreferencesIntoUi();
            UpdateLibraryActionButtonsEnabled();
            RefreshPlaybackNormalizationStatsLine();
        }

        private void RefreshPlaybackNormalizationStatsLine()
        {
            if (Owner is not MainWindow mw)
            {
                PlaybackLoudnormCacheStatsText.Text = "—";
                return;
            }

            try
            {
                var total = mw.CopyLibraryFilePathsForLoudnormStats().Count;
                PlaybackLoudnormCacheStatsText.Text = $"0/{total}";
            }
            catch
            {
                PlaybackLoudnormCacheStatsText.Text = "—";
            }
        }

        private async Task LoadFileStorageMusicPathsAsync()
        {
            try
            {
                var folders = await LibraryManager.Instance.GetMusicFoldersAsync();
                string display;
                if (folders == null || folders.Count == 0)
                {
                    display = "-";
                    _fileStorageMusicPathOpen = "";
                }
                else
                {
                    display = string.Join(Environment.NewLine, folders);
                    _fileStorageMusicPathOpen = folders[0] ?? "";
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    FileStorageMusicLibraryPathText.Text = display ?? "";
                    FileStorageOpenMusicLibraryButton.IsEnabled = CanOpenAboutFolder(_fileStorageMusicPathOpen);
                });
            }
            catch
            {
                _fileStorageMusicPathOpen = "";
                await Dispatcher.InvokeAsync(() =>
                {
                    FileStorageMusicLibraryPathText.Text = "-";
                    FileStorageOpenMusicLibraryButton.IsEnabled = false;
                });
            }
        }

        private void FileStorageOpenMusicLibrary_Click(object sender, RoutedEventArgs e)
        {
            OpenFolderInExplorer(_fileStorageMusicPathOpen);
        }

        private void FileStorageOpenSettingsPath_Click(object sender, RoutedEventArgs e)
        {
            OpenFolderInExplorer(_aboutSettingsPath);
        }

        private void UpdateLibraryActionButtonsEnabled()
        {
            var canRunLibraryActions = Owner is MainWindow && !_libraryAlbumArtScanGuiBusy;
            LibraryAddMusicButton.IsEnabled = canRunLibraryActions;
            LibraryRescanLibraryButton.IsEnabled = canRunLibraryActions;
            LibraryRemoveMusicButton.IsEnabled = canRunLibraryActions;
            LibraryClearSettingsButton.IsEnabled = canRunLibraryActions;
            LibraryScanMissingAlbumArtButton.IsEnabled = canRunLibraryActions;
        }

        private MainWindow? OwnerMainWhenLibraryIdle()
        {
            if (_libraryAlbumArtScanGuiBusy) return null;
            return Owner as MainWindow;
        }

        private async void LibraryAddMusicButton_Click(object sender, RoutedEventArgs e)
        {
            var main = OwnerMainWhenLibraryIdle();
            if (main != null)
                await main.RunAddMusicFromSettingsAsync();
        }

        private async void LibraryRescanLibraryButton_Click(object sender, RoutedEventArgs e)
        {
            var main = OwnerMainWhenLibraryIdle();
            if (main != null)
                await main.RunRescanLibraryFromSettingsAsync();
        }

        private async void LibraryRemoveMusicButton_Click(object sender, RoutedEventArgs e)
        {
            var main = OwnerMainWhenLibraryIdle();
            if (main != null)
                await main.RunRemoveMusicFromSettingsAsync();
        }

        private void LibraryClearSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var main = OwnerMainWhenLibraryIdle();
            if (main != null)
                main.RunClearSettingsFromSettings();
        }

        private void CleanupLibraryAlbumArtScanGuiState()
        {
            _libraryAlbumArtScanGuiBusy = false;
            LibraryAlbumArtScanProgressPanel.Visibility = Visibility.Collapsed;
            LibraryScanMissingAlbumArtButton.Content = "Scan";
            UpdateLibraryActionButtonsEnabled();
        }

        private async void LibraryScanMissingAlbumArtButton_Click(object sender, RoutedEventArgs e)
        {
            if (_libraryAlbumArtScanGuiBusy || Owner is not MainWindow main)
                return;

            _libraryAlbumArtScanGuiBusy = true;
            LibraryScanMissingAlbumArtButton.Content = "Scanning...";
            LibraryAlbumArtScanProgressStatusText.Text = "Starting scan…";
            LibraryAlbumArtScanProgressBar.IsIndeterminate = true;
            LibraryAlbumArtScanProgressBar.Value = 0;
            LibraryAlbumArtScanProgressPanel.Visibility = Visibility.Visible;
            UpdateLibraryActionButtonsEnabled();

            var progress = new Progress<RemoteAlbumArtScanUiProgress>(p =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    LibraryAlbumArtScanProgressStatusText.Text = p.Message;
                    if (p.Total < 0)
                    {
                        LibraryAlbumArtScanProgressBar.IsIndeterminate = true;
                        return;
                    }

                    if (LibraryAlbumArtScanProgressBar.IsIndeterminate)
                        LibraryAlbumArtScanProgressBar.IsIndeterminate = false;
                    LibraryAlbumArtScanProgressBar.Maximum = p.Total;
                    LibraryAlbumArtScanProgressBar.Value = Math.Min(p.Done, p.Total);
                }), DispatcherPriority.Background);
            });

            try
            {
                var (ok, skipped, failed) =
                    await main.RunScanMissingRemoteAlbumArtAsync(progress, CancellationToken.None).ConfigureAwait(true);

                LibraryAlbumArtScanProgressStatusText.Text = ok == 0 && failed == 0
                    ? $"Done. Nothing new to embed (skipped {skipped})."
                    : $"Done. Saved {ok}, skipped {skipped}, failed {failed}.";
            }
            catch (OperationCanceledException)
            {
                LibraryAlbumArtScanProgressStatusText.Text = "Canceled.";
            }
            catch (Exception ex)
            {
                LibraryAlbumArtScanProgressStatusText.Text = $"Error: {ex.Message}";
            }
            finally
            {
                _libraryAlbumArtScanGuiBusy = false;
                LibraryScanMissingAlbumArtButton.Content = "Scan";
                LibraryAlbumArtScanProgressBar.IsIndeterminate = false;
                UpdateLibraryActionButtonsEnabled();
            }
        }

        private void SidebarLibraryActionShowInSidebar_CheckChanged(object sender, RoutedEventArgs e)
        {
            if (_sidebarLibraryActionCheckLoading)
                return;
            PreferencesManager.EnsureInitialized(_preferences);
            _preferences.Sidebar.ShowAddMusic = SidebarAddMusicCheckBox.IsChecked == true;
            _preferences.Sidebar.ShowRescanLibrary = SidebarRescanLibraryCheckBox.IsChecked == true;
            _preferences.Sidebar.ShowRemoveMusic = SidebarRemoveMusicCheckBox.IsChecked == true;
            _preferences.Sidebar.ShowClearSettings = SidebarClearSettingsCheckBox.IsChecked == true;
            PreferencesManager.Instance.SavePreferencesSync(_preferences);
            if (Owner is MainWindow main)
                main.ApplySidebarPreferences();
        }

        private void LoadPreferencesIntoUi()
        {
            _preferences = PreferencesManager.Instance.LoadPreferencesSync();
            PreferencesManager.EnsureInitialized(_preferences);

            var g = _preferences.General;
            GeneralCheckForUpdatesCheckBox.IsChecked = g.CheckForUpdates;
            GeneralAutoInstallUpdatesCheckBox.IsChecked = g.AutomaticallyInstallUpdates;
            GeneralLaunchAfterUpdateCheckBox.IsChecked = g.LaunchAppAfterUpdate;

            var lang = g.Language ?? AppLanguageCatalog.SystemLanguageTag;
            var langItem = GeneralLanguageComboBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(i => i.Tag is string s && s == lang);
            GeneralLanguageComboBox.SelectedItem = langItem
                ?? GeneralLanguageComboBox.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(i => i.Tag is string s && s == AppLanguageCatalog.SystemLanguageTag);

            var sb = _preferences.Sidebar;
            _sidebarLibraryActionCheckLoading = true;
            try
            {
                SidebarAddMusicCheckBox.IsChecked = sb.ShowAddMusic;
                SidebarRescanLibraryCheckBox.IsChecked = sb.ShowRescanLibrary;
                SidebarRemoveMusicCheckBox.IsChecked = sb.ShowRemoveMusic;
                SidebarClearSettingsCheckBox.IsChecked = sb.ShowClearSettings;
            }
            finally
            {
                _sidebarLibraryActionCheckLoading = false;
            }

            _playbackNormalizationCheckLoading = true;
            try
            {
                PlaybackVolumeNormalizationCheckBox.IsChecked = _preferences.Playback.VolumeNormalization;
            }
            finally
            {
                _playbackNormalizationCheckLoading = false;
            }

            PlaybackCrossfadeSlider.ValueChanged -= PlaybackCrossfadeSlider_ValueChanged;
            PlaybackCrossfadeSlider.Value = _preferences.Playback.CrossfadeSeconds;
            PlaybackCrossfadeSlider.ValueChanged += PlaybackCrossfadeSlider_ValueChanged;

            _playbackCrossfadeRampTextLoading = true;
            try
            {
                PlaybackCrossfadeRampTextBox.Text = _preferences.Playback.CrossfadeRampSeconds.ToString(
                    "0.######",
                    CultureInfo.CurrentCulture);
            }
            finally
            {
                _playbackCrossfadeRampTextLoading = false;
            }

            _playbackAudioOutputUiLoading = true;
            try
            {
                var want = _preferences.Playback.AudioBackend.ToString();
                var backendItem = PlaybackAudioBackendComboBox.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(i => i.Tag is string t && t == want);
                PlaybackAudioBackendComboBox.SelectedItem = backendItem
                    ?? PlaybackAudioBackendComboBox.Items.OfType<ComboBoxItem>()
                        .FirstOrDefault(i => i.Tag is string t && t == AudioOutputBackend.WasapiShared.ToString());

                PlaybackSoftwareVolumeCheckBox.IsChecked = !_preferences.Playback.UseSoftwareSessionVolume;

                var rateHz = _preferences.Playback.OutputSampleRateHz;
                var rateTag = rateHz.ToString(CultureInfo.InvariantCulture);
                var rateItem = PlaybackOutputSampleRateComboBox.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(i => i.Tag is string t && t == rateTag);
                PlaybackOutputSampleRateComboBox.SelectedItem = rateItem
                    ?? PlaybackOutputSampleRateComboBox.Items.OfType<ComboBoxItem>()
                        .FirstOrDefault(i => i.Tag is string t && t == "48000");

                var wantBits = _preferences.Playback.OutputBits.ToString();
                var bitsItem = PlaybackOutputBitsComboBox.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(i => i.Tag is string t && t == wantBits);
                PlaybackOutputBitsComboBox.SelectedItem = bitsItem
                    ?? PlaybackOutputBitsComboBox.Items.OfType<ComboBoxItem>()
                        .FirstOrDefault(i => i.Tag is string t && t == nameof(PlaybackOutputBits.Pcm16));
            }
            finally
            {
                _playbackAudioOutputUiLoading = false;
            }
        }

        private bool _playbackCrossfadeRampTextLoading;
        private bool _playbackAudioOutputUiLoading;

        private void PlaybackCrossfadeRampTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_playbackCrossfadeRampTextLoading)
                return;
            CommitPlaybackCrossfadeRampFromTextBox();
        }

        private void CommitPlaybackCrossfadeRampFromTextBox()
        {
            if (!double.TryParse(
                    PlaybackCrossfadeRampTextBox.Text.Trim(),
                    NumberStyles.Float,
                    CultureInfo.CurrentCulture,
                    out var v))
            {
                v = Math.Clamp((int)Math.Round(PlaybackCrossfadeSlider.Value), 0, 15) <= 0 ? 0d : 2d;
            }

            v = Math.Clamp(v, 0, 120d);
            if (Math.Clamp((int)Math.Round(PlaybackCrossfadeSlider.Value), 0, 15) <= 0)
                v = 0;
            PreferencesManager.EnsureInitialized(_preferences);
            _preferences.Playback.CrossfadeRampSeconds = v;
            _playbackCrossfadeRampTextLoading = true;
            try
            {
                PlaybackCrossfadeRampTextBox.Text = v.ToString("0.######", CultureInfo.CurrentCulture);
            }
            finally
            {
                _playbackCrossfadeRampTextLoading = false;
            }

            PreferencesManager.Instance.SavePreferencesSync(_preferences);
            NotifyMainWindowIfOwner();
        }

        private void ReadUiIntoPreferences()
        {
            PreferencesManager.EnsureInitialized(_preferences);

            _preferences.General.CheckForUpdates = GeneralCheckForUpdatesCheckBox.IsChecked == true;
            _preferences.General.AutomaticallyInstallUpdates = GeneralAutoInstallUpdatesCheckBox.IsChecked == true;
            _preferences.General.LaunchAppAfterUpdate = GeneralLaunchAfterUpdateCheckBox.IsChecked == true;
            if (GeneralLanguageComboBox.SelectedItem is ComboBoxItem langItem && langItem.Tag is string code)
                _preferences.General.Language = code;
            else
                _preferences.General.Language = AppLanguageCatalog.SystemLanguageTag;

            _preferences.Sidebar.ShowAddMusic = SidebarAddMusicCheckBox.IsChecked == true;
            _preferences.Sidebar.ShowRescanLibrary = SidebarRescanLibraryCheckBox.IsChecked == true;
            _preferences.Sidebar.ShowRemoveMusic = SidebarRemoveMusicCheckBox.IsChecked == true;
            _preferences.Sidebar.ShowClearSettings = SidebarClearSettingsCheckBox.IsChecked == true;

            _preferences.Playback.VolumeNormalization = PlaybackVolumeNormalizationCheckBox.IsChecked == true;
            var crossfadeSec = Math.Clamp((int)Math.Round(PlaybackCrossfadeSlider.Value), 0, 15);
            _preferences.Playback.CrossfadeSeconds = crossfadeSec;
            if (crossfadeSec <= 0)
                _preferences.Playback.CrossfadeRampSeconds = 0;
            else if (double.TryParse(
                         PlaybackCrossfadeRampTextBox.Text.Trim(),
                         NumberStyles.Float,
                         CultureInfo.CurrentCulture,
                         out var ramp))
                _preferences.Playback.CrossfadeRampSeconds = Math.Clamp(ramp, 0, 120d);

            if (PlaybackAudioBackendComboBox.SelectedItem is ComboBoxItem bi && bi.Tag is string tag &&
                Enum.TryParse<AudioOutputBackend>(tag, out var backend))
                _preferences.Playback.AudioBackend = backend;
            else
                _preferences.Playback.AudioBackend = AudioOutputBackend.WasapiShared;

            _preferences.Playback.UseSoftwareSessionVolume = PlaybackSoftwareVolumeCheckBox.IsChecked != true;

            if (PlaybackOutputSampleRateComboBox.SelectedItem is ComboBoxItem rateItem && rateItem.Tag is string rateTag &&
                int.TryParse(rateTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rateParsed))
                _preferences.Playback.OutputSampleRateHz = PlaybackResampler.NormalizeOutputSampleRateHz(rateParsed);
            else
                _preferences.Playback.OutputSampleRateHz = PlaybackResampler.DefaultOutputSampleRateHz;

            if (PlaybackOutputBitsComboBox.SelectedItem is ComboBoxItem bitsItem && bitsItem.Tag is string bitsTag &&
                Enum.TryParse<PlaybackOutputBits>(bitsTag, out var bitsParsed))
                _preferences.Playback.OutputBits = PlaybackOutputBitsUtil.Normalize(bitsParsed);
            else
                _preferences.Playback.OutputBits = PlaybackOutputBitsUtil.Default;
        }

        private void PlaybackCrossfadeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            PreferencesManager.EnsureInitialized(_preferences);
            var n = Math.Clamp((int)Math.Round(PlaybackCrossfadeSlider.Value), 0, 15);
            _preferences.Playback.CrossfadeSeconds = n;

            if (n <= 0)
            {
                _preferences.Playback.CrossfadeRampSeconds = 0;
                _playbackCrossfadeRampTextLoading = true;
                try
                {
                    PlaybackCrossfadeRampTextBox.Text = 0d.ToString("0.######", CultureInfo.CurrentCulture);
                }
                finally
                {
                    _playbackCrossfadeRampTextLoading = false;
                }
            }
            else if (n >= 1)
            {
                var defaultRamp = CrossfadeParameters.GetDefaultRampSecondsForOverlap(n);
                _preferences.Playback.CrossfadeRampSeconds = Math.Clamp(defaultRamp, 0, 120d);
                _playbackCrossfadeRampTextLoading = true;
                try
                {
                    PlaybackCrossfadeRampTextBox.Text = defaultRamp.ToString("0.######", CultureInfo.CurrentCulture);
                }
                finally
                {
                    _playbackCrossfadeRampTextLoading = false;
                }
            }

            PreferencesManager.Instance.SavePreferencesSync(_preferences);
            NotifyMainWindowIfOwner();
        }

        private void PlaybackVolumeNormalizationCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_playbackNormalizationCheckLoading) return;
            PreferencesManager.EnsureInitialized(_preferences);
            _preferences.Playback.VolumeNormalization = PlaybackVolumeNormalizationCheckBox.IsChecked == true;
            PreferencesManager.Instance.SavePreferencesSync(_preferences);
            NotifyMainWindowIfOwner();
            RefreshPlaybackNormalizationStatsLine();
        }

        private void PlaybackAudioInfrastructure_Changed(object sender, RoutedEventArgs e)
        {
            if (_playbackAudioOutputUiLoading) return;
            PreferencesManager.EnsureInitialized(_preferences);
            if (PlaybackAudioBackendComboBox.SelectedItem is ComboBoxItem bi && bi.Tag is string tag &&
                Enum.TryParse<AudioOutputBackend>(tag, out var backend))
                _preferences.Playback.AudioBackend = backend;
            else
                _preferences.Playback.AudioBackend = AudioOutputBackend.WasapiShared;

            _preferences.Playback.UseSoftwareSessionVolume = PlaybackSoftwareVolumeCheckBox.IsChecked != true;

            PreferencesManager.Instance.SavePreferencesSync(_preferences);
            NotifyMainWindowIfOwner();
        }

        private void PlaybackOutputSampleRateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_playbackAudioOutputUiLoading)
                return;
            if (PlaybackOutputSampleRateComboBox.SelectedItem is not ComboBoxItem rateItem || rateItem.Tag is not string rateTag)
                return;
            if (!int.TryParse(rateTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hz))
                return;

            PreferencesManager.EnsureInitialized(_preferences);
            _preferences.Playback.OutputSampleRateHz = PlaybackResampler.NormalizeOutputSampleRateHz(hz);
            PreferencesManager.Instance.SavePreferencesSync(_preferences);
            NotifyMainWindowIfOwner();
        }

        private void PlaybackOutputBitsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_playbackAudioOutputUiLoading)
                return;
            if (PlaybackOutputBitsComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string tag)
                return;
            if (!Enum.TryParse<PlaybackOutputBits>(tag, out var bits))
                return;

            PreferencesManager.EnsureInitialized(_preferences);
            _preferences.Playback.OutputBits = PlaybackOutputBitsUtil.Normalize(bits);
            PreferencesManager.Instance.SavePreferencesSync(_preferences);
            NotifyMainWindowIfOwner();
        }

        private void ApplyAboutTitle()
        {
            var doc = new FlowDocument
            {
                PagePadding = new Thickness(0),
                TextAlignment = TextAlignment.Left,
                FontFamily = TextElement.GetFontFamily(AboutTitleRichTextBox),
                FontSize = TextElement.GetFontSize(AboutTitleRichTextBox),
                Foreground = AboutTitleRichTextBox.Foreground
            };
            var link = new Hyperlink(new Run("musicApp"))
            {
                NavigateUri = new Uri("https://github.com/fosterbarnes/musicApp", UriKind.Absolute)
            };
            if (AboutTitleRichTextBox.TryFindResource("AboutHyperlinkStyle") is Style aboutLinkStyle)
                link.Style = aboutLinkStyle;
            link.FontWeight = FontWeights.SemiBold;
            if (AboutTitleRichTextBox.TryFindResource("TextPrimary-brush") is Brush primaryBrush)
                link.Foreground = primaryBrush;
            link.RequestNavigate += AboutHyperlink_RequestNavigate;

            var para = new Paragraph { Margin = new Thickness(0) };
            para.Inlines.Add(link);

            var suffix = AppVersionFiles.GetAboutTitleSuffix();
            if (!string.IsNullOrEmpty(suffix))
            {
                var run = new Run(suffix)
                {
                    FontWeight = FontWeights.SemiBold
                };
                if (AboutTitleRichTextBox.TryFindResource("TextPrimary-brush") is Brush p)
                    run.Foreground = p;
                para.Inlines.Add(run);
            }

            doc.Blocks.Add(para);
            AboutTitleRichTextBox.Document = doc;
        }

        private void ApplyAboutVersionSubtitle()
        {
            var text = AppVersionFiles.GetAboutVersionSubtitle();
            var url = AppVersionFiles.GetGitHubReleaseUrlForCurrentVersion();
            var doc = new FlowDocument
            {
                PagePadding = new Thickness(0),
                TextAlignment = TextAlignment.Left,
                FontFamily = TextElement.GetFontFamily(AboutVersionSubtitleText),
                FontSize = TextElement.GetFontSize(AboutVersionSubtitleText),
                Foreground = AboutVersionSubtitleText.Foreground
            };
            var link = new Hyperlink(new Run(text))
            {
                NavigateUri = new Uri(url, UriKind.Absolute)
            };
            if (AboutVersionSubtitleText.TryFindResource("AboutHyperlinkStyle") is Style aboutLinkStyle)
                link.Style = aboutLinkStyle;
            if (AboutVersionSubtitleText.TryFindResource("TextLinkStrong-brush") is Brush linkBrush)
                link.Foreground = linkBrush;
            link.RequestNavigate += AboutHyperlink_RequestNavigate;
            doc.Blocks.Add(new Paragraph(link) { Margin = new Thickness(0) });
            AboutVersionSubtitleText.Document = doc;
        }

        private static bool CanOpenAboutFolder(string? path)
        {
            return !string.IsNullOrWhiteSpace(path) && path != "-";
        }

        private static void OpenFolderInExplorer(string path)
        {
            if (!CanOpenAboutFolder(path))
                return;
            try
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch
            {
            }
        }

        private void AboutOpenInstallLocation_Click(object sender, RoutedEventArgs e)
        {
            OpenFolderInExplorer(_aboutInstallPath);
        }

        private void AboutOpenSettingsLocation_Click(object sender, RoutedEventArgs e)
        {
            OpenFolderInExplorer(_aboutSettingsPath);
        }

        private static void SetAboutPathRichText(RichTextBox box, string text)
        {
            var doc = new FlowDocument
            {
                PagePadding = new Thickness(0),
                TextAlignment = TextAlignment.Left,
                Foreground = box.Foreground,
                FontFamily = TextElement.GetFontFamily(box),
                FontSize = TextElement.GetFontSize(box)
            };
            var p = new Paragraph(new Run(text ?? string.Empty)) { Margin = new Thickness(0) };
            doc.Blocks.Add(p);
            box.Document = doc;
        }

        private void NotifyMainWindowIfOwner()
        {
            if (Owner is MainWindow main)
            {
                main.ApplySidebarPreferences();
                main.ApplyPlaybackPreferences();
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            ReadUiIntoPreferences();
            PreferencesManager.Instance.SavePreferencesSync(_preferences);
            NotifyMainWindowIfOwner();
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            ReadUiIntoPreferences();
            PreferencesManager.Instance.SavePreferencesSync(_preferences);
            NotifyMainWindowIfOwner();
        }

        private void AboutHyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private void AboutCreditLineHyperlink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Hyperlink h && h.NavigateUri != null)
                Process.Start(new ProcessStartInfo(h.NavigateUri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private static void BalanceKeyboardShortcutColumns(ListView listView)
        {
            if (listView.View is not GridView gv || gv.Columns.Count < 2)
            {
                return;
            }

            var w = listView.ActualWidth;
            if (w <= 0 || double.IsNaN(w))
            {
                return;
            }

            var colW = Math.Max(40, (w - 8) / 2.0);
            gv.Columns[0].Width = colW;
            gv.Columns[1].Width = colW;
        }

        private void BalanceAllKeyboardShortcutColumns()
        {
            BalanceKeyboardShortcutColumns(KeyboardShortcutsInAppListView);
            BalanceKeyboardShortcutColumns(KeyboardShortcutsGlobalListView);
        }

        private void SectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string sectionName)
            {
                return;
            }

            ShowSection(sectionName);
        }

        private void ShowSection(string sectionName)
        {
            GeneralSectionPanel.Visibility = sectionName == "General" ? Visibility.Visible : Visibility.Collapsed;
            PlaybackSectionPanel.Visibility = sectionName == "Playback" ? Visibility.Visible : Visibility.Collapsed;
            LibrarySectionPanel.Visibility = sectionName == "Library" ? Visibility.Visible : Visibility.Collapsed;
            ThemeSectionPanel.Visibility = sectionName == "Theme" ? Visibility.Visible : Visibility.Collapsed;
            KeyboardShortcutsSectionPanel.Visibility = sectionName == "KeyboardShortcuts" ? Visibility.Visible : Visibility.Collapsed;
            AboutSectionPanel.Visibility = sectionName == "About" ? Visibility.Visible : Visibility.Collapsed;

            if (sectionName == "KeyboardShortcuts")
            {
                Dispatcher.BeginInvoke(new Action(BalanceAllKeyboardShortcutColumns), DispatcherPriority.Loaded);
            }

            var idx = Array.IndexOf(SettingsSectionOrder, sectionName);
            SectionSegmentUi.ApplySegmentStates(
                this,
                new[]
                {
                    GeneralSectionButton,
                    PlaybackSectionButton,
                    LibrarySectionButton,
                    KeyboardShortcutsSectionButton,
                    ThemeSectionButton,
                    AboutSectionButton
                },
                idx < 0 ? 0 : idx);

            if (sectionName == "Playback")
                RefreshPlaybackNormalizationStatsLine();

        }
    }
}
