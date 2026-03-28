using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using NAudio.Wave;
using musicApp;

namespace musicApp.TitleBarPlus
{
    public partial class TitleBar : System.Windows.Controls.UserControl
    {
        // === Events ===
        public event EventHandler? PlayPauseRequested;
        public event EventHandler? PreviousTrackRequested;
        public event EventHandler? NextTrackRequested;
        public event EventHandler? WindowMinimizeRequested;
        public event EventHandler? WindowMaximizeRequested;
        public event EventHandler? WindowCloseRequested;
        public event EventHandler<double>? VolumeChanged;
        public event EventHandler<bool>? ShuffleStateChanged;
        /// <summary>Raised when search text changes (debounced). EventArgs is the current query; empty string when cleared or placeholder.</summary>
        public event EventHandler<string>? SearchTextChanged;
        public event EventHandler<string>? ArtistNavigationRequested;
        public event EventHandler<string>? AlbumNavigationRequested;
        public event EventHandler? QueuePopupToggleRequested;
        /// <summary>Fired after the user commits a new position on the seek bar (drag release).</summary>
        public event EventHandler? PlaybackPositionCommitted;

        // === Audio Playback State ===
        private IWavePlayer? waveOut;
        private AudioFileReader? audioFileReader;
        private bool _useSoftwareSessionVolume = true;
        private bool isPlaying = false;
        private bool isMuted = false;
        private double previousVolume = 50;
        private DispatcherTimer? seekBarTimer;
        private TimeSpan totalDuration;
        private TimeSpan pausedPosition;
        private bool isUpdatingAudioObjects = false;

        // === Player Settings State ===
        private bool isShuffleEnabled = false;
        private SettingsManager.RepeatMode repeatMode = SettingsManager.RepeatMode.Off;

        // === Constants ===
        private const double MIN_SPACING_FROM_EDGE = 20;
        private const double MAX_SPACING_FROM_EDGE = 50;
        private const double MIN_SPACING_BETWEEN_CONTROLS = 20;
        private const double MAX_SPACING_BETWEEN_CONTROLS = 50;
        private const double MIN_WINDOW_WIDTH_FOR_SPACING = 1039;
        private const double MAX_WINDOW_WIDTH_FOR_SPACING = 1600;

        private const double BUTTON_FILL_SIZE = 24.0;
        private const int ANIMATION_DURATION_MS = 200;
        private const int MOUSE_MOVE_DELAY_MS = 50;
        private const double MOUSE_POSITION_TOLERANCE = 100;
        private const double VOLUME_MAX = 100;
        private const double FALLBACK_PLAYBACK_CONTROLS_WIDTH = 146;
        private const double WINDOW_CONTROLS_WIDTH = 140;
        private const double SEARCH_BAR_RIGHT_MARGIN_OFFSET = -134;
        private const double GRADIENT_MASK_WIDTH = 120;
        private const double GRADIENT_MASK_HEIGHT = 38;
        private const double REPEAT_ICON_OFFSET_Y = -1.5;
        private const double SEARCH_PLACEHOLDER_COLOR_R = 204;
        private const double SEARCH_PLACEHOLDER_COLOR_G = 204;
        private const double SEARCH_PLACEHOLDER_COLOR_B = 204;
        private const int SEARCH_DEBOUNCE_MS = 300;
        private const string SEARCH_PLACEHOLDER_TEXT = "Search";

        private DispatcherTimer? _searchDebounceTimer;

        // === Properties ===
        public bool IsPlaying
        {
            get => isPlaying;
            set
            {
                isPlaying = value;
                UpdatePlayPauseIcon();
                UpdateSeekBarTimer();

                // Safety check: if we're stopping playback, ensure the seek bar timer is stopped
                if (!isPlaying)
                {
                    StopSeekBarTimer();

                    // Additional safety: if we're stopping and have no valid audio objects, reset to initial state
                    if (!AreAudioObjectsValid())
                    {
                        ResetToInitialState();
                    }
                }
            }
        }

        public double Volume
        {
            get => sliderVolume.Value;
            set
            {
                sliderVolume.Value = Math.Max(0, Math.Min(VOLUME_MAX, value));
                if (waveOut != null && !isMuted)
                    ApplyOutputVolumeFromUi(sliderVolume.Value);
            }
        }

        public bool IsMuted
        {
            get => isMuted;
            set
            {
                isMuted = value;
                UpdateVolumeIcon();
            }
        }

        public bool IsShuffleEnabled
        {
            get => isShuffleEnabled;
            set
            {
                if (isShuffleEnabled != value)
                {
                    isShuffleEnabled = value;
                    UpdateShuffleIcon();
                    ShuffleStateChanged?.Invoke(this, isShuffleEnabled);
                }
            }
        }

        public SettingsManager.RepeatMode RepeatMode
        {
            get => repeatMode;
            set
            {
                repeatMode = value;
                UpdateRepeatIcon();
            }
        }

        public bool IsRepeatEnabled
        {
            get => repeatMode != SettingsManager.RepeatMode.Off;
        }

        /// <summary>
        /// Gets the current playback position
        /// </summary>
        public TimeSpan CurrentPosition
        {
            get
            {
                try
                {
                    if (AreAudioObjectsValid() && audioFileReader != null)
                    {
                        return audioFileReader.CurrentTime;
                    }
                    return pausedPosition;
                }
                catch
                {
                    // If we encounter an error getting the current position, reset to initial state
                    Console.WriteLine("Error getting current position - resetting to initial state");
                    ResetToInitialState();
                    return TimeSpan.Zero;
                }
            }
        }

        // === Constructor ===
        public TitleBar()
        {
            InitializeComponent();
            this.Loaded += TitleBar_Loaded;
            this.Unloaded += TitleBar_Unloaded;
            InitializeSeekBarTimer();

            if (txtSearch != null)
            {
                txtSearch.Text = SEARCH_PLACEHOLDER_TEXT;
                txtSearch.Foreground = new SolidColorBrush(Color.FromRgb((byte)SEARCH_PLACEHOLDER_COLOR_R, (byte)SEARCH_PLACEHOLDER_COLOR_G, (byte)SEARCH_PLACEHOLDER_COLOR_B));
                txtSearch.GotFocus += TxtSearch_GotFocus;
                txtSearch.LostFocus += TxtSearch_LostFocus;
                txtSearch.TextChanged += TxtSearch_TextChanged;
            }
            _searchDebounceTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(SEARCH_DEBOUNCE_MS)
            };
            _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;
            if (btnSearchClear != null)
                btnSearchClear.Visibility = Visibility.Collapsed;
        }

        /// <summary>Returns the current search query; empty string if placeholder or blank.</summary>
        public string GetSearchQuery()
        {
            if (txtSearch == null) return "";
            var t = txtSearch.Text?.Trim() ?? "";
            return (t == "" || t == SEARCH_PLACEHOLDER_TEXT) ? "" : t;
        }

        /// <summary>Search bar border for popup placement.</summary>
        public Border? SearchBarBorder => searchBarBorder;

        public FrameworkElement? QueuePopupPlacementTarget => btnQueue;

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchDebounceTimer?.Stop();
            if (txtSearch == null) return;
            if (btnSearchClear != null)
                btnSearchClear.Visibility = string.IsNullOrWhiteSpace(GetSearchQuery()) ? Visibility.Collapsed : Visibility.Visible;
            var query = GetSearchQuery();
            if (query.Length == 0)
            {
                SearchTextChanged?.Invoke(this, "");
                return;
            }
            _searchDebounceTimer?.Start();
        }

        private void SearchDebounceTimer_Tick(object? sender, EventArgs e)
        {
            _searchDebounceTimer?.Stop();
            var query = GetSearchQuery();
            SearchTextChanged?.Invoke(this, query);
        }

        private void BtnSearchClear_Click(object sender, RoutedEventArgs e)
        {
            if (txtSearch == null) return;
            txtSearch.Text = SEARCH_PLACEHOLDER_TEXT;
            txtSearch.Foreground = new SolidColorBrush(Color.FromRgb((byte)SEARCH_PLACEHOLDER_COLOR_R, (byte)SEARCH_PLACEHOLDER_COLOR_G, (byte)SEARCH_PLACEHOLDER_COLOR_B));
            if (btnSearchClear != null) btnSearchClear.Visibility = Visibility.Collapsed;
            SearchTextChanged?.Invoke(this, "");
        }

        private void TxtSearch_GotFocus(object sender, RoutedEventArgs e)
        {
            if (txtSearch != null && txtSearch.Text == SEARCH_PLACEHOLDER_TEXT)
            {
                txtSearch.Text = "";
                txtSearch.Foreground = new SolidColorBrush(Colors.White);
            }
        }

        private void TxtSearch_LostFocus(object sender, RoutedEventArgs e)
        {
            if (txtSearch != null && string.IsNullOrWhiteSpace(txtSearch.Text))
            {
                txtSearch.Text = SEARCH_PLACEHOLDER_TEXT;
                txtSearch.Foreground = new SolidColorBrush(Color.FromRgb((byte)SEARCH_PLACEHOLDER_COLOR_R, (byte)SEARCH_PLACEHOLDER_COLOR_G, (byte)SEARCH_PLACEHOLDER_COLOR_B));
            }
        }

        // === Window Control Events ===
        private async void TitleBar_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_searchDebounceTimer != null)
                {
                    _searchDebounceTimer.Tick -= SearchDebounceTimer_Tick;
                    _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;
                }

                if (seekBarTimer == null)
                    InitializeSeekBarTimer();

                var window = Window.GetWindow(this);
                if (window != null)
                {
                    window.SizeChanged -= Window_SizeChanged;
                    window.SizeChanged += Window_SizeChanged;
                    UpdateSongInfoWidth();
                }

                _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => {
                    UpdateSeekBarWidth();
                    UpdateGradientMask();
                    var window = Window.GetWindow(this);
                    if (window != null)
                    {
                        UpdateControlSpacing(window.ActualWidth);
                    }
                }));

                await LoadPlayerSettingsAsync();
            }
            catch (Exception ex)
            {
                HandleError("TitleBar_Loaded", ex);
            }
        }

        private void TitleBar_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var window = Window.GetWindow(this);
                if (window != null)
                    window.SizeChanged -= Window_SizeChanged;

                if (_searchDebounceTimer != null)
                {
                    _searchDebounceTimer.Stop();
                    _searchDebounceTimer.Tick -= SearchDebounceTimer_Tick;
                }

                if (seekBarTimer != null)
                {
                    seekBarTimer.Stop();
                    seekBarTimer.Tick -= SeekBarTimer_Tick;
                    seekBarTimer = null;
                }
            }
            catch (Exception ex)
            {
                HandleError("TitleBar_Unloaded", ex);
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                UpdateSongInfoWidth();
                UpdateSeekBarWidth();
                UpdateGradientMask();

                if (audioFileReader != null && totalDuration.TotalSeconds > 0)
                {
                    try
                    {
                        if (progressFill != null)
                        {
                            double progress = audioFileReader.CurrentTime.TotalSeconds / totalDuration.TotalSeconds;
                            double progressWidth = currentSeekBarWidth * progress;
                            progressFill.Width = Math.Max(0, Math.Min(currentSeekBarWidth, progressWidth));
                        }
                    }
                    catch (ObjectDisposedException) { }
                    catch (NullReferenceException) { }
                }
            }
            catch (Exception ex)
            {
                HandleError("Window_SizeChanged", ex);
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.Source == seekBarBackground || seekBarBackground.IsMouseOver)
                return;

            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.ClickCount == 2)
                {
                    WindowMaximizeRequested?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    Window.GetWindow(this)?.DragMove();
                }
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowMinimizeRequested?.Invoke(this, EventArgs.Empty);
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            WindowMaximizeRequested?.Invoke(this, EventArgs.Empty);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            WindowCloseRequested?.Invoke(this, EventArgs.Empty);
        }

        // === Playback Control Events ===
        private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            PlayPauseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void BtnPrevious_Click(object sender, RoutedEventArgs e)
        {
            PreviousTrackRequested?.Invoke(this, EventArgs.Empty);
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            NextTrackRequested?.Invoke(this, EventArgs.Empty);
        }

        // === Shuffle and Repeat Button Events ===
        private async void BtnShuffle_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            IsShuffleEnabled = !IsShuffleEnabled;
            await SettingsManager.Instance.SetShuffleStateAsync(IsShuffleEnabled);
        }

        private async void BtnRepeat_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            var newMode = repeatMode switch
            {
                SettingsManager.RepeatMode.Off => SettingsManager.RepeatMode.All,
                SettingsManager.RepeatMode.All => SettingsManager.RepeatMode.One,
                SettingsManager.RepeatMode.One => SettingsManager.RepeatMode.Off,
                _ => SettingsManager.RepeatMode.Off
            };

            RepeatMode = newMode;
            await SettingsManager.Instance.SetRepeatModeAsync(newMode);
        }

        // === Button Animation Helper Methods ===
        private void AnimateFillExpand(System.Windows.FrameworkElement fillElement)
        {
            if (fillElement != null)
            {
                var animation = new System.Windows.Media.Animation.DoubleAnimation
                {
                    To = BUTTON_FILL_SIZE,
                    Duration = TimeSpan.FromMilliseconds(ANIMATION_DURATION_MS),
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };

                fillElement.BeginAnimation(WidthProperty, animation);
                fillElement.BeginAnimation(HeightProperty, animation);
            }
        }

        private void AnimateFillContract(System.Windows.FrameworkElement fillElement)
        {
            if (fillElement != null)
            {
                var animation = new System.Windows.Media.Animation.DoubleAnimation
                {
                    To = 0.0,
                    Duration = TimeSpan.FromMilliseconds(ANIMATION_DURATION_MS),
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };

                fillElement.BeginAnimation(WidthProperty, animation);
                fillElement.BeginAnimation(HeightProperty, animation);
            }
        }

        // === Queue Button Events ===
        private void BtnQueue_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            QueuePopupToggleRequested?.Invoke(this, EventArgs.Empty);
        }

        // === Song Info Navigation Events ===
        private void TxtCurrentArtist_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var artist = txtCurrentArtist?.Text;
            if (!string.IsNullOrWhiteSpace(artist))
            {
                e.Handled = true;
                ArtistNavigationRequested?.Invoke(this, artist);
            }
        }

        private void TxtCurrentAlbum_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var album = txtCurrentAlbum?.Text;
            if (!string.IsNullOrWhiteSpace(album))
            {
                e.Handled = true;
                AlbumNavigationRequested?.Invoke(this, album);
            }
        }

        // === Volume Control Events ===
        private void SliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ApplyOutputVolumeFromUi(sliderVolume.Value);
        }

        private void IconVolume_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (isMuted)
            {
                isMuted = false;
                iconVolume.Kind = PackIconKind.VolumeHigh;
                sliderVolume.Value = previousVolume;
            }
            else
            {
                isMuted = true;
                previousVolume = sliderVolume.Value;
                iconVolume.Kind = PackIconKind.VolumeOff;
                sliderVolume.Value = 0;
            }
        }

        // === Public Methods ===
        public void SetTrackInfo(string title, string artist, string? album = null, BitmapImage? albumArt = null)
        {
            if (txtCurrentTrack != null)
                txtCurrentTrack.Text = title ?? "No track selected";
            if (txtCurrentArtist != null)
                txtCurrentArtist.Text = artist ?? "";
            if (txtCurrentAlbum != null)
                txtCurrentAlbum.Text = album ?? "";

            bool hasAlbum = !string.IsNullOrEmpty(album);
            if (txtDashSeparator != null)
                txtDashSeparator.Visibility = hasAlbum ? Visibility.Visible : Visibility.Collapsed;
            if (txtCurrentAlbum != null)
                txtCurrentAlbum.Visibility = hasAlbum ? Visibility.Visible : Visibility.Collapsed;

            if (imgAlbumArt != null)
            {
                imgAlbumArt.Source = albumArt;
            }
        }

        public void SetAudioObjects(IWavePlayer? waveOut, AudioFileReader? audioFileReader, bool useSoftwareSessionVolume = true)
        {
            isUpdatingAudioObjects = true;

            try
            {
                seekBarTimer?.Stop();

                this.waveOut = waveOut;
                this.audioFileReader = audioFileReader;
                _useSoftwareSessionVolume = useSoftwareSessionVolume;

                if (waveOut == null || audioFileReader == null)
                {
                    ResetToInitialState();
                }
                else
                {
                    if (!isMuted)
                        ApplyOutputVolumeFromUi(sliderVolume.Value);

                    try
                    {
                        totalDuration = audioFileReader.TotalTime;
                        var pos = audioFileReader.CurrentTime;
                        if (pos < TimeSpan.Zero)
                            pos = TimeSpan.Zero;
                        if (totalDuration > TimeSpan.Zero && pos > totalDuration)
                            pos = totalDuration;
                        pausedPosition = TimeSpan.Zero;
                        UpdateSeekBar(pos, totalDuration);
                    }
                    catch (ObjectDisposedException)
                    {
                        totalDuration = TimeSpan.Zero;
                        pausedPosition = TimeSpan.Zero;
                        UpdateSeekBar(TimeSpan.Zero, TimeSpan.Zero);
                    }
                }
            }
            catch (Exception ex)
            {
                HandleError("SetAudioObjects", ex);
            }
            finally
            {
                isUpdatingAudioObjects = false;
                UpdateSeekBarTimer();
            }
        }

        public void ResetToInitialState()
        {
            try
            {
                seekBarTimer?.Stop();

                totalDuration = TimeSpan.Zero;
                pausedPosition = TimeSpan.Zero;
                isPlaying = false;

                isDragging = false;
                dragTargetPosition = TimeSpan.Zero;

                UpdateSeekBar(TimeSpan.Zero, TimeSpan.Zero);
                UpdatePlayPauseIcon();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during TitleBar reset: {ex.Message}");
            }
        }

        private void StopSeekBarTimer()
        {
            try
            {
                seekBarTimer?.Stop();
            }
            catch (Exception ex)
            {
                HandleError("StopSeekBarTimer", ex);
            }
        }

        public void UpdateWindowStateIcon(WindowState state)
        {
            if (state == WindowState.Maximized)
            {
                iconMaximize.Kind = PackIconKind.WindowRestore;
            }
            else
            {
                iconMaximize.Kind = PackIconKind.WindowMaximize;
            }
        }

        public PackIconKind GetCurrentWindowStateIcon()
        {
            return iconMaximize.Kind;
        }

        // === Private Helper Methods ===
        private async Task LoadPlayerSettingsAsync()
        {
            try
            {
                IsShuffleEnabled = await SettingsManager.Instance.GetShuffleStateAsync();
                RepeatMode = await SettingsManager.Instance.GetRepeatModeAsync();

                double restoredVol = await SettingsManager.Instance.GetTitleBarVolume0To100Async();
                sliderVolume.Value = Math.Max(0, Math.Min(VOLUME_MAX, restoredVol));
                previousVolume = restoredVol > 0 ? restoredVol : 100;

                UpdateShuffleIcon();
                UpdateRepeatIcon();
                ShuffleStateChanged?.Invoke(this, IsShuffleEnabled);
            }
            catch (Exception ex)
            {
                HandleError("LoadPlayerSettingsAsync", ex);
            }
        }

        private void UpdatePlayPauseIcon()
        {
            try
            {
                iconPlayPause.Kind = isPlaying ? PackIconKind.Pause : PackIconKind.Play;
            }
            catch (Exception ex)
            {
                HandleError("UpdatePlayPauseIcon", ex);
            }
        }

        private void UpdateVolumeIcon()
        {
            try
            {
                iconVolume.Kind = isMuted ? PackIconKind.VolumeOff : PackIconKind.VolumeHigh;
            }
            catch (Exception ex)
            {
                HandleError("UpdateVolumeIcon", ex);
            }
        }

        private void UpdateShuffleIcon()
        {
            try
            {
                if (IsShuffleEnabled)
                {
                    iconShuffle.Kind = PackIconKind.ShuffleVariant;
                    AnimateFillExpand(btnShuffleFill);
                }
                else
                {
                    iconShuffle.Kind = PackIconKind.ShuffleVariant;
                    AnimateFillContract(btnShuffleFill);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in UpdateShuffleIcon: {ex.Message}");
            }
        }

        private void UpdateRepeatIcon()
        {
            try
            {
                switch (RepeatMode)
                {
                    case SettingsManager.RepeatMode.Off:
                        iconRepeat.Source = Application.Current.Resources["RepeatStandardIcon"] as ImageSource;
                        AnimateFillContract(btnRepeatFill);
                        ResetRepeatIconTransform();
                        break;

                    case SettingsManager.RepeatMode.All:
                        iconRepeat.Source = Application.Current.Resources["RepeatStandardIcon"] as ImageSource;
                        AnimateFillExpand(btnRepeatFill);
                        ResetRepeatIconTransform();
                        break;

                    case SettingsManager.RepeatMode.One:
                        var customIcon = Application.Current.Resources["RepeatOneIcon"] as ImageSource;
                        iconRepeat.Source = customIcon ?? Application.Current.Resources["RepeatStandardIcon"] as ImageSource;
                        
                        if (customIcon != null && iconRepeat.RenderTransform is TranslateTransform transform)
                        {
                            transform.Y = REPEAT_ICON_OFFSET_Y;
                        }

                        AnimateFillExpand(btnRepeatFill);
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in UpdateRepeatIcon: {ex.Message}");
            }
        }

        private void ResetRepeatIconTransform()
        {
            if (iconRepeat?.RenderTransform is TranslateTransform transform)
            {
                transform.Y = 0;
            }
        }

        private void UpdateSongInfoWidth()
        {
            var window = Window.GetWindow(this);
            if (window == null) return;

            double windowWidth = window.ActualWidth;
            double calculatedWidth = CalculateResponsiveWidth(windowWidth);

            var songInfoBorder = this.FindName("songInfoBorder") as Border;
            if (songInfoBorder != null)
            {
                songInfoBorder.Width = calculatedWidth;
                UpdateSongInfoPosition(windowWidth, calculatedWidth);
            }

            UpdateGradientMask();
            UpdateControlSpacing(windowWidth);
        }

        private void UpdateSeekBarWidth()
        {
            if (seekBarBackground != null)
            {
                currentSeekBarWidth = seekBarBackground.ActualWidth;
            }
        }

        private void UpdateSongInfoPosition(double windowWidth, double songInfoWidth)
        {
            var songInfoBorder = this.FindName("songInfoBorder") as Border;
            if (songInfoBorder == null) return;

            songInfoBorder.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            songInfoBorder.Margin = new Thickness(0, 5, 0, 5);
        }

        private double CalculateResponsiveWidth(double windowWidth)
        {
            const double MIN_WIDTH = 300;
            const double MAX_WIDTH = 600;
            const double MIN_WINDOW_WIDTH = 1039;
            const double MAX_WINDOW_WIDTH = 1600;

            if (windowWidth <= MIN_WINDOW_WIDTH)
                return MIN_WIDTH;
            if (windowWidth >= MAX_WINDOW_WIDTH)
                return MAX_WIDTH;

            double windowRange = MAX_WINDOW_WIDTH - MIN_WINDOW_WIDTH;
            double widthRange = MAX_WIDTH - MIN_WIDTH;
            double progress = (windowWidth - MIN_WINDOW_WIDTH) / windowRange;
            return MIN_WIDTH + (widthRange * progress);
        }

        private double CalculateVolumeSliderWidth(double windowWidth)
        {
            const double MIN_SLIDER_WIDTH = 60;
            const double MAX_SLIDER_WIDTH = 100;
            const double SONG_INFO_MIN_WINDOW_WIDTH = 1039;
            const double VOLUME_SLIDER_MIN_WINDOW_WIDTH = 800;

            if (windowWidth >= SONG_INFO_MIN_WINDOW_WIDTH)
                return MAX_SLIDER_WIDTH;
            if (windowWidth <= VOLUME_SLIDER_MIN_WINDOW_WIDTH)
                return MIN_SLIDER_WIDTH;

            double windowRange = SONG_INFO_MIN_WINDOW_WIDTH - VOLUME_SLIDER_MIN_WINDOW_WIDTH;
            double widthRange = MAX_SLIDER_WIDTH - MIN_SLIDER_WIDTH;
            double progress = (windowWidth - VOLUME_SLIDER_MIN_WINDOW_WIDTH) / windowRange;
            return MIN_SLIDER_WIDTH + (widthRange * progress);
        }

        private double CalculateSearchWidth(double windowWidth)
        {
            const double MIN_SEARCH_WIDTH = 150;
            const double MAX_SEARCH_WIDTH = 300;
            const double MIN_WINDOW_WIDTH = 1039;
            const double MAX_WINDOW_WIDTH = 1600;

            if (windowWidth <= MIN_WINDOW_WIDTH)
                return MIN_SEARCH_WIDTH;
            if (windowWidth >= MAX_WINDOW_WIDTH)
                return MAX_SEARCH_WIDTH;

            double windowRange = MAX_WINDOW_WIDTH - MIN_WINDOW_WIDTH;
            double widthRange = MAX_SEARCH_WIDTH - MIN_SEARCH_WIDTH;
            double progress = (windowWidth - MIN_WINDOW_WIDTH) / windowRange;
            return MIN_SEARCH_WIDTH + (widthRange * progress);
        }

        private double CalculateSpacingFromEdge(double windowWidth)
        {
            if (windowWidth <= MIN_WINDOW_WIDTH_FOR_SPACING)
                return MIN_SPACING_FROM_EDGE;
            if (windowWidth >= MAX_WINDOW_WIDTH_FOR_SPACING)
                return MAX_SPACING_FROM_EDGE;

            double windowRange = MAX_WINDOW_WIDTH_FOR_SPACING - MIN_WINDOW_WIDTH_FOR_SPACING;
            double spacingRange = MAX_SPACING_FROM_EDGE - MIN_SPACING_FROM_EDGE;
            double progress = (windowWidth - MIN_WINDOW_WIDTH_FOR_SPACING) / windowRange;
            return MIN_SPACING_FROM_EDGE + (spacingRange * progress);
        }

        private double CalculateSpacingBetweenControls(double windowWidth)
        {
            if (windowWidth <= MIN_WINDOW_WIDTH_FOR_SPACING)
                return MIN_SPACING_BETWEEN_CONTROLS;
            if (windowWidth >= MAX_WINDOW_WIDTH_FOR_SPACING)
                return MAX_SPACING_BETWEEN_CONTROLS;

            double windowRange = MAX_WINDOW_WIDTH_FOR_SPACING - MIN_WINDOW_WIDTH_FOR_SPACING;
            double spacingRange = MAX_SPACING_BETWEEN_CONTROLS - MIN_SPACING_BETWEEN_CONTROLS;
            double progress = (windowWidth - MIN_WINDOW_WIDTH_FOR_SPACING) / windowRange;
            return MIN_SPACING_BETWEEN_CONTROLS + (spacingRange * progress);
        }

        private void UpdateControlSpacing(double windowWidth)
        {
            if (playbackControlsPanel == null || volumeControlsPanel == null) return;
            if (windowWidth <= 0) return;

            double spacingFromEdge = CalculateSpacingFromEdge(windowWidth);
            double spacingBetweenControls = CalculateSpacingBetweenControls(windowWidth);

            if (sliderVolume != null)
            {
                sliderVolume.Width = CalculateVolumeSliderWidth(windowWidth);
            }

            playbackControlsPanel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            volumeControlsPanel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            
            double playbackControlsWidth = playbackControlsPanel.DesiredSize.Width;
            if (playbackControlsWidth <= 0) playbackControlsWidth = FALLBACK_PLAYBACK_CONTROLS_WIDTH;

            playbackControlsPanel.Margin = new Thickness(spacingFromEdge, 0, 0, 0);
            
            double volumeLeftEdge = spacingFromEdge + playbackControlsWidth + spacingBetweenControls;
            volumeControlsPanel.Margin = new Thickness(volumeLeftEdge, 0, 0, 0);

            if (searchBarBorder != null)
            {
                double searchBarWidth = CalculateSearchWidth(windowWidth);
                searchBarBorder.Width = searchBarWidth;
                double searchRightMargin = WINDOW_CONTROLS_WIDTH + SEARCH_BAR_RIGHT_MARGIN_OFFSET;
                searchBarBorder.Margin = new Thickness(0, 0, searchRightMargin, 0);
            }
        }

        private void UpdateGradientMask()
        {
            if (textGradientMask == null) return;

            var window = Window.GetWindow(this);
            if (window == null) return;

            textGradientMask.Width = GRADIENT_MASK_WIDTH;
            textGradientMask.Height = GRADIENT_MASK_HEIGHT;
            textGradientMask.Margin = new Thickness(0, 0, 0, 0);
        }

        public void UpdateSeekBar(TimeSpan currentTime, TimeSpan totalTime)
        {
            try
            {
                if (txtCurrentTime != null)
                    txtCurrentTime.Text = FormatTimeSpan(currentTime);
                if (txtTotalDuration != null)
                    txtTotalDuration.Text = FormatTimeSpan(totalTime);

                totalDuration = totalTime;

                if (progressFill != null)
                {
                    if (totalTime.TotalSeconds > 0)
                    {
                        double progress = currentTime.TotalSeconds / totalTime.TotalSeconds;
                        double progressWidth = currentSeekBarWidth * progress;
                        progressFill.Width = Math.Max(0, Math.Min(currentSeekBarWidth, progressWidth));
                    }
                    else
                    {
                        progressFill.Width = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                HandleError("UpdateSeekBar", ex);
            }
        }



        private string FormatTimeSpan(TimeSpan timeSpan)
        {
            return $"{(int)timeSpan.TotalMinutes}:{timeSpan.Seconds:D2}";
        }

        private void InitializeSeekBarTimer()
        {
            try
            {
                seekBarTimer = new DispatcherTimer();
                seekBarTimer.Interval = TimeSpan.FromSeconds(1);
                seekBarTimer.Tick += SeekBarTimer_Tick;
            }
            catch (Exception ex)
            {
                HandleError("InitializeSeekBarTimer", ex);
            }
        }

        private bool AreAudioObjectsValid()
        {
            try
            {
                if (audioFileReader == null)
                    return false;

                var _ = audioFileReader.TotalTime;
                return true;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (NullReferenceException)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }

        private void UpdateSeekBarTimer()
        {
            if (isUpdatingAudioObjects || seekBarTimer == null) return;

            seekBarTimer.Stop();

            try
            {
                if (isPlaying && AreAudioObjectsValid())
                {
                    seekBarTimer.Start();
                }
                else
                {
                    if (AreAudioObjectsValid() && audioFileReader != null)
                    {
                        try
                        {
                            pausedPosition = audioFileReader.CurrentTime;
                        }
                        catch
                        {
                            pausedPosition = TimeSpan.Zero;
                        }
                    }
                    else
                    {
                        pausedPosition = TimeSpan.Zero;
                        if (!isPlaying)
                        {
                            ResetToInitialState();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                HandleError("UpdateSeekBarTimer", ex);
            }
        }

        private void SeekBarTimer_Tick(object? sender, EventArgs e)
        {
            if (isUpdatingAudioObjects || isDragging) return;

            if (!isPlaying || !AreAudioObjectsValid())
            {
                seekBarTimer?.Stop();
                if (!isPlaying && !AreAudioObjectsValid())
                {
                    ResetToInitialState();
                }
                return;
            }

            try
            {
                if (audioFileReader != null)
                {
                    UpdateSeekBar(audioFileReader.CurrentTime, totalDuration);
                }
                else
                {
                    seekBarTimer?.Stop();
                }
            }
            catch (ObjectDisposedException)
            {
                seekBarTimer?.Stop();
            }
            catch (NullReferenceException)
            {
                seekBarTimer?.Stop();
            }
            catch (Exception ex)
            {
                seekBarTimer?.Stop();
                HandleError("SeekBarTimer_Tick", ex);
            }
        }

        // === Seek Bar Interaction Events ===
        private bool isDragging = false;
        private double currentSeekBarWidth = 0;
        private bool wasMutedBeforeDrag = false;
        private double volumeBeforeDrag = 100;
        private System.Windows.Point lastValidMousePosition;
        private DateTime lastMouseDownTime;
        private TimeSpan dragTargetPosition;

        private void SeekBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (audioFileReader == null || totalDuration.TotalSeconds <= 0) return;

            System.Windows.Point clickPoint = e.GetPosition(seekBarBackground);
            double clickPosition = ClampMousePosition(clickPoint.X, currentSeekBarWidth);

            lastValidMousePosition = clickPoint;
            lastMouseDownTime = DateTime.Now;

            double progress = Math.Max(0, Math.Min(1, clickPosition / currentSeekBarWidth));
            dragTargetPosition = TimeSpan.FromSeconds(progress * totalDuration.TotalSeconds);

            if (audioFileReader != null && totalDuration.TotalSeconds > 0)
            {
                try
                {
                    UpdateSeekBar(dragTargetPosition, totalDuration);
                }
                catch (ObjectDisposedException) { }
                catch (NullReferenceException) { }
            }

            wasMutedBeforeDrag = isMuted;
            volumeBeforeDrag = sliderVolume.Value;

            if (waveOut != null && !isMuted)
            {
                if (_useSoftwareSessionVolume)
                    VolumeChanged?.Invoke(this, 0);
                else
                    TrySetDeviceVolume(waveOut, 0f);
            }

            isDragging = true;
            seekBarBackground.CaptureMouse();
            e.Handled = true;
        }

        private void SeekBar_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!isDragging || audioFileReader == null || totalDuration.TotalSeconds <= 0) return;

            TimeSpan timeSinceMouseDown = DateTime.Now - lastMouseDownTime;
            if (timeSinceMouseDown.TotalMilliseconds < MOUSE_MOVE_DELAY_MS)
                return;

            System.Windows.Point currentPoint = e.GetPosition(seekBarBackground);
            double currentPosition = currentPoint.X;

            if (currentPosition < -MOUSE_POSITION_TOLERANCE || currentPosition > currentSeekBarWidth + MOUSE_POSITION_TOLERANCE)
                return;

            currentPosition = ClampMousePosition(currentPosition, currentSeekBarWidth);
            lastValidMousePosition = currentPoint;

            double progress = Math.Max(0, Math.Min(1, currentPosition / currentSeekBarWidth));
            dragTargetPosition = TimeSpan.FromSeconds(progress * totalDuration.TotalSeconds);

            if (audioFileReader != null && totalDuration.TotalSeconds > 0)
            {
                try
                {
                    UpdateSeekBar(dragTargetPosition, totalDuration);
                }
                catch (ObjectDisposedException) { }
                catch (NullReferenceException) { }
            }
        }

        private void SeekBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!isDragging) return;

            EndDragOperation();
            e.Handled = true;
        }

        private void SeekBar_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
        }

        private void ApplyOutputVolumeFromUi(double slider0To100)
        {
            if (!_useSoftwareSessionVolume && waveOut != null && !isMuted)
                TrySetDeviceVolume(waveOut, (float)(slider0To100 / VOLUME_MAX));
            VolumeChanged?.Invoke(this, slider0To100);
        }

        private static void TrySetDeviceVolume(IWavePlayer? player, float linear0To1)
        {
            switch (player)
            {
                case WaveOutEvent w:
                    w.Volume = linear0To1;
                    break;
                case WasapiOut w:
                    w.Volume = linear0To1;
                    break;
                case DirectSoundOut d:
                    d.Volume = linear0To1;
                    break;
            }
        }

        private double ClampMousePosition(double position, double maxWidth)
        {
            return Math.Max(0, Math.Min(maxWidth, position));
        }

        private void EndDragOperation()
        {
            if (waveOut != null)
            {
                if (_useSoftwareSessionVolume)
                {
                    if (wasMutedBeforeDrag)
                        VolumeChanged?.Invoke(this, 0);
                    else
                        VolumeChanged?.Invoke(this, volumeBeforeDrag);
                }
                else
                {
                    if (wasMutedBeforeDrag)
                        TrySetDeviceVolume(waveOut, 0f);
                    else
                        TrySetDeviceVolume(waveOut, (float)(volumeBeforeDrag / VOLUME_MAX));
                }
            }

            if (audioFileReader != null && totalDuration.TotalSeconds > 0)
            {
                try
                {
                    audioFileReader.CurrentTime = dragTargetPosition;
                    UpdateSeekBar(dragTargetPosition, totalDuration);
                }
                catch (ObjectDisposedException) { }
                catch (NullReferenceException) { }
            }

            isDragging = false;
            seekBarBackground.ReleaseMouseCapture();
            PlaybackPositionCommitted?.Invoke(this, EventArgs.Empty);
        }

        private void HandleError(string context, Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in {context}: {ex.Message}");
            ResetToInitialState();
        }

    }
}