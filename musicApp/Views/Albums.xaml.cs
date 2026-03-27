using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using musicApp.Constants;
using musicApp.Helpers;

namespace musicApp.Views
{
    public partial class AlbumsView : UserControl
    {
        private const double AlbumTileMinSize = 96;
        private const double AlbumTileMaxSize = 220;
        private const double DefaultTileSize = 158d;
        private const double BaseTileRightGap = 42;
        private const double BaseTileBottomGap = 16;
        private const double BaseFlyoutGap = 35;
        public const double FlyoutHeight = 340;
        public const double FlyoutArtSize = 280;
        private const int BaseFlyoutRowsPerColumn = 10;
        private const double FlyoutTrackRowHeight = 24;

        public double AlbumSizePercent
        {
            get => (double)GetValue(AlbumSizePercentProperty);
            set => SetValue(AlbumSizePercentProperty, value);
        }

        public static readonly DependencyProperty AlbumSizePercentProperty =
            DependencyProperty.Register(
                nameof(AlbumSizePercent),
                typeof(double),
                typeof(AlbumsView),
                new FrameworkPropertyMetadata(
                    50d,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnAlbumSizePercentChanged));

        public double CurrentTileSize
        {
            get => (double)GetValue(CurrentTileSizeProperty);
            set => SetValue(CurrentTileSizeProperty, value);
        }

        public static readonly DependencyProperty CurrentTileSizeProperty =
            DependencyProperty.Register(
                nameof(CurrentTileSize),
                typeof(double),
                typeof(AlbumsView),
                new PropertyMetadata(DefaultTileSize, OnCurrentTileSizeChanged));

        public Thickness TileMargin
        {
            get => (Thickness)GetValue(TileMarginProperty);
            set => SetValue(TileMarginProperty, value);
        }

        public static readonly DependencyProperty TileMarginProperty =
            DependencyProperty.Register(
                nameof(TileMargin),
                typeof(Thickness),
                typeof(AlbumsView),
                new PropertyMetadata(new Thickness(0, 0, BaseTileRightGap, BaseTileBottomGap)));

        public double TileScaleRatio
        {
            get => (double)GetValue(TileScaleRatioProperty);
            set => SetValue(TileScaleRatioProperty, value);
        }

        public static readonly DependencyProperty TileScaleRatioProperty =
            DependencyProperty.Register(
                nameof(TileScaleRatio),
                typeof(double),
                typeof(AlbumsView),
                new PropertyMetadata(1.0));

        public double FlyoutPanelWidth
        {
            get => (double)GetValue(FlyoutPanelWidthProperty);
            set => SetValue(FlyoutPanelWidthProperty, value);
        }

        public static readonly DependencyProperty FlyoutPanelWidthProperty =
            DependencyProperty.Register(
                nameof(FlyoutPanelWidth),
                typeof(double),
                typeof(AlbumsView),
                new PropertyMetadata(600d));

        public Thickness FlyoutMargin
        {
            get => (Thickness)GetValue(FlyoutMarginProperty);
            set => SetValue(FlyoutMarginProperty, value);
        }

        public static readonly DependencyProperty FlyoutMarginProperty =
            DependencyProperty.Register(
                nameof(FlyoutMargin),
                typeof(Thickness),
                typeof(AlbumsView),
                new PropertyMetadata(new Thickness(0, Math.Max(0, BaseFlyoutGap - BaseTileBottomGap), 0, BaseFlyoutGap)));

        public Thickness FlyoutArrowOffset
        {
            get => (Thickness)GetValue(FlyoutArrowOffsetProperty);
            set => SetValue(FlyoutArrowOffsetProperty, value);
        }

        public static readonly DependencyProperty FlyoutArrowOffsetProperty =
            DependencyProperty.Register(
                nameof(FlyoutArrowOffset),
                typeof(Thickness),
                typeof(AlbumsView),
                new PropertyMetadata(new Thickness(0)));

        public double FlyoutPanelHeight
        {
            get => (double)GetValue(FlyoutPanelHeightProperty);
            set => SetValue(FlyoutPanelHeightProperty, value);
        }

        public static readonly DependencyProperty FlyoutPanelHeightProperty =
            DependencyProperty.Register(
                nameof(FlyoutPanelHeight),
                typeof(double),
                typeof(AlbumsView),
                new PropertyMetadata(FlyoutHeight));

        private static void OnCurrentTileSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AlbumsView view)
            {
                double ratio = (double)e.NewValue / DefaultTileSize;
                double tileBottom = Math.Round(BaseTileBottomGap * ratio);
                double flyoutGap = Math.Round(BaseFlyoutGap * ratio);
                view.TileMargin = new Thickness(0, 0, Math.Round(BaseTileRightGap * ratio), tileBottom);
                view.FlyoutMargin = new Thickness(0, Math.Max(0, flyoutGap - tileBottom), 0, flyoutGap);
                view.TileScaleRatio = ratio;
            }
        }

        private static void OnAlbumSizePercentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not AlbumsView view)
                return;

            double percent = Math.Clamp((double)e.NewValue, 0d, 100d);
            double newSize = AlbumTileMinSize + (percent / 100d) * (AlbumTileMaxSize - AlbumTileMinSize);

            if (view._isDraggingSlider && view._dragScaleTransform != null)
            {
                view._dragTargetSize = newSize;
                double scale = newSize / view._dragStartTileSize;
                view._dragScaleTransform.ScaleX = scale;
                view._dragScaleTransform.ScaleY = scale;
            }
            else
            {
                view.CurrentTileSize = newSize;
                view.ScheduleArtLoad();
            }
        }

        private enum AlbumSortMode { Album, Artist }

        private AlbumSortMode _sortMode = AlbumSortMode.Album;
        private IEnumerable? _itemsSource;
        private int _itemsSourceCount = -1;
        private ObservableCollection<object> _albumItems = new();
        private CancellationTokenSource? _rebuildCts;
        private CancellationTokenSource? _viewportArtCts;
        private CancellationTokenSource? _prefetchArtCts;
        private Song? _contextMenuSong;
        
        private (string albumName, string? artistName, bool openDetails, string? selectedTrackFilePath)? _pendingAlbumSelection;
        private readonly DispatcherTimer _artLoadDebounce;
        private readonly DispatcherTimer _flyoutResizeDebounce;
        private readonly DispatcherTimer _resizeAnchorDebounce;

        private AlbumGridItem? _selectedAlbum;
        private AlbumFlyoutItem? _currentFlyout;
        private bool _isRefreshingFlyoutLayout;
        private ResizeAnchorState? _pendingResizeAnchor;

        private bool _isDraggingSlider;
        private double _dragStartTileSize;
        private double _dragTargetSize;
        private ScaleTransform? _dragScaleTransform;
        
        private enum ResizeAnchorKind
        {
            SelectedAlbum,
            FirstVisibleAlbum
        }

        private readonly record struct ResizeAnchorState(
            string AlbumTitle,
            string Artist,
            double OffsetFromViewportTop,
            ResizeAnchorKind Kind);

        public static readonly DependencyProperty SelectedFlyoutTrackFilePathProperty =
            DependencyProperty.Register(
                nameof(SelectedFlyoutTrackFilePath),
                typeof(string),
                typeof(AlbumsView),
                new PropertyMetadata(null));

        public string? SelectedFlyoutTrackFilePath
        {
            get => (string?)GetValue(SelectedFlyoutTrackFilePathProperty);
            set => SetValue(SelectedFlyoutTrackFilePathProperty, value);
        }

        public AlbumsView()
        {
            InitializeComponent();
            AlbumGrid.ItemsSource = _albumItems;

            _artLoadDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _artLoadDebounce.Tick += async (_, __) =>
            {
                _artLoadDebounce.Stop();
                await LoadVisibleAlbumArtAsync();
            };

            _flyoutResizeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
            _flyoutResizeDebounce.Tick += (_, __) =>
            {
                _flyoutResizeDebounce.Stop();
                RefreshOpenFlyoutLayout();
            };
            _resizeAnchorDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
            _resizeAnchorDebounce.Tick += (_, __) =>
            {
                _resizeAnchorDebounce.Stop();
                RestoreResizeAnchorIfPending();
            };

            Loaded += (_, __) => KickViewportAlbumArtNow();
            IsVisibleChanged += (_, __) =>
            {
                if (IsVisible)
                    KickViewportAlbumArtNow();
            };
            Unloaded += AlbumsView_OnUnloaded;
            SizeChanged += AlbumsView_SizeChanged;
        }

        private void AlbumsView_OnUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _rebuildCts?.Cancel();
                _rebuildCts?.Dispose();
                _rebuildCts = null;
                _viewportArtCts?.Cancel();
                _viewportArtCts?.Dispose();
                _viewportArtCts = null;
                _prefetchArtCts?.Cancel();
                _prefetchArtCts?.Dispose();
                _prefetchArtCts = null;
                _artLoadDebounce.Stop();
                _flyoutResizeDebounce.Stop();
                _resizeAnchorDebounce.Stop();
            }
            catch
            {
                // ignore
            }
        }

        private void ScheduleArtLoad()
        {
            _artLoadDebounce.Stop();
            _artLoadDebounce.Start();
        }

        /// <summary>Starts a viewport art pass ASAP (no debounce). Safe from any thread.</summary>
        private void KickViewportAlbumArtNow()
        {
            _artLoadDebounce.Stop();
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(KickViewportAlbumArtNow, DispatcherPriority.Loaded);
                return;
            }
            _ = LoadVisibleAlbumArtAsync();
        }

        private void CancelAllAlbumArtWork()
        {
            _viewportArtCts?.Cancel();
            _viewportArtCts?.Dispose();
            _viewportArtCts = null;
            _prefetchArtCts?.Cancel();
            _prefetchArtCts?.Dispose();
            _prefetchArtCts = null;
        }

        private void ShowLoadingIndicator()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(ShowLoadingIndicator); return; }
            LoadingIndicator.Visibility = Visibility.Visible;
            StartBounceAnimation();
        }

        private void HideLoadingIndicator()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(HideLoadingIndicator); return; }
            StopBounceAnimation();
            LoadingIndicator.Visibility = Visibility.Collapsed;
        }

        private void StartBounceAnimation()
        {
            var dots = new[] { LoadDot0, LoadDot1, LoadDot2, LoadDot3 };
            double bounce = 8;
            double step = 1000.0 / 6.0;

            for (int i = 0; i < dots.Length; i++)
            {
                var anim = new DoubleAnimationUsingKeyFrames
                {
                    Duration = TimeSpan.FromSeconds(1),
                    RepeatBehavior = RepeatBehavior.Forever
                };

                double offset = i * step;
                if (offset > 0)
                    anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(offset))));
                anim.KeyFrames.Add(new LinearDoubleKeyFrame(-bounce, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(offset + step))));
                anim.KeyFrames.Add(new LinearDoubleKeyFrame(bounce, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(offset + step * 2))));
                anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(offset + step * 3))));

                ((TranslateTransform)dots[i].RenderTransform).BeginAnimation(TranslateTransform.YProperty, anim);
            }
        }

        private void StopBounceAnimation()
        {
            var dots = new[] { LoadDot0, LoadDot1, LoadDot2, LoadDot3 };
            foreach (var dot in dots)
                ((TranslateTransform)dot.RenderTransform).BeginAnimation(TranslateTransform.YProperty, null);
        }

        public IEnumerable? ItemsSource
        {
            get => _itemsSource;
            set
            {
                int newCount = TryGetCount(value);
                if (ReferenceEquals(_itemsSource, value) && newCount == _itemsSourceCount && _albumItems.Count > 0)
                    return;

                _itemsSource = value;
                _itemsSourceCount = newCount;
                _ = RebuildAlbumItemsAsync(preserveViewState: true);
            }
        }

        public event EventHandler<AlbumGridItem>? AlbumClicked;

        public event EventHandler<Song>? PlayTrackRequested;
        public event EventHandler<(Song track, Playlist playlist)>? AddTrackToPlaylistRequested;
        public event EventHandler<Song>? CreateNewPlaylistWithTrackRequested;
        public event EventHandler<Song>? PlayNextRequested;
        public event EventHandler<Song>? AddToQueueRequested;
        public event EventHandler<Song>? InfoRequested;
        public event EventHandler<Song>? ShowInArtistsRequested;
        public event EventHandler<Song>? ShowInAlbumsRequested;
        public event EventHandler<Song>? ShowInSongsRequested;
        public event EventHandler<Song>? ShowInQueueRequested;
        public event EventHandler<Song>? ShowInExplorerRequested;
        public event EventHandler<Song>? RemoveFromLibraryRequested;
        public event EventHandler<Song>? DeleteRequested;
        public event EventHandler<string>? ArtistNavigationRequested;
        public event EventHandler<string>? GenreNavigationRequested;

        public void RebuildColumns() { }

        public void RefreshAlbumGridFromLibrary() => _ = RebuildAlbumItemsAsync(preserveViewState: true);

        public bool TryRefreshAlbumGroupInPlace(Song track)
        {
            if (track == null || !TryGetAlbumGridGroupKey(track, out var albumTitle, out var artistKey))
                return false;

            int targetSize = Math.Max(UILayoutConstants.AlbumArtMinimumTargetSize, (int)Math.Round(CurrentTileSize));
            var matchedAny = false;

            foreach (var item in _albumItems.OfType<AlbumGridItem>())
            {
                if (!string.Equals(item.AlbumTitle, albumTitle, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(item.Artist, artistKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                matchedAny = true;
                var rep = item.RepresentativeTrack;
                var img = rep != null ? AlbumArtThumbnailHelper.LoadForTrack(rep, targetSize) : null;
                item.AlbumArtSource = img;
            }

            if (!matchedAny)
                return false;

            if (_currentFlyout != null &&
                string.Equals(_currentFlyout.AlbumTitle, albumTitle, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_currentFlyout.Artist, artistKey, StringComparison.OrdinalIgnoreCase))
            {
                PatchOpenFlyoutForGroup(albumTitle, artistKey);
            }

            KickViewportAlbumArtNow();
            return true;
        }

        public void ScrollToAlbum(string albumName)
        {
            SelectAlbum(albumName, null, openDetails: true, selectedTrackFilePath: null);
        }

        public void SelectAlbum(Song track)
        {
            if (track == null || string.IsNullOrWhiteSpace(track.Album))
                return;

            string? artist = !string.IsNullOrWhiteSpace(track.AlbumArtist) ? track.AlbumArtist : track.Artist;
            SelectAlbum(track.Album, artist, openDetails: true, selectedTrackFilePath: track.FilePath);
        }

        public void SelectAlbum(string albumName, string? artistName, bool openDetails)
            => SelectAlbum(albumName, artistName, openDetails, selectedTrackFilePath: null);

        public void SelectAlbum(string albumName, string? artistName, bool openDetails, string? selectedTrackFilePath)
        {
            if (string.IsNullOrWhiteSpace(albumName))
                return;

            if (string.IsNullOrWhiteSpace(selectedTrackFilePath))
                SelectedFlyoutTrackFilePath = null;

            var item = FindAlbumItem(albumName, artistName);
            if (item == null)
            {
                _pendingAlbumSelection = (albumName, artistName, openDetails, selectedTrackFilePath);
                return;
            }

            _pendingAlbumSelection = null;
            SelectAlbumItem(item, openDetails, bringTargetIntoView: true);

            SelectedFlyoutTrackFilePath = selectedTrackFilePath;
        }

        private void ApplyMergedAlbumSelection(
            (string albumName, string? artistName, bool openDetails, string? selectedTrackFilePath) p,
            bool bringTargetIntoView)
        {
            if (string.IsNullOrWhiteSpace(p.albumName))
                return;

            if (string.IsNullOrWhiteSpace(p.selectedTrackFilePath))
                SelectedFlyoutTrackFilePath = null;

            var item = FindAlbumItem(p.albumName, p.artistName);
            if (item == null)
            {
                _pendingAlbumSelection = p;
                return;
            }

            _pendingAlbumSelection = null;
            SelectAlbumItem(item, p.openDetails, bringTargetIntoView);
            SelectedFlyoutTrackFilePath = p.selectedTrackFilePath;
        }

        private AlbumGridItem? FindAlbumItem(string albumName, string? artistName)
        {
            if (!string.IsNullOrWhiteSpace(artistName))
            {
                var match = _albumItems.OfType<AlbumGridItem>().FirstOrDefault(a =>
                    string.Equals(a.AlbumTitle, albumName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(a.Artist, artistName, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match;
            }

            return _albumItems.OfType<AlbumGridItem>().FirstOrDefault(a =>
                string.Equals(a.AlbumTitle, albumName, StringComparison.OrdinalIgnoreCase));
        }

        private void SelectAlbumItem(AlbumGridItem album, bool openDetails, bool bringTargetIntoView = true)
        {
            if (openDetails)
            {
                if (!ReferenceEquals(_selectedAlbum, album) || _currentFlyout == null)
                    ShowAlbumDetail(album, bringFlyoutIntoView: bringTargetIntoView);
                else
                    RefreshOpenFlyoutLayout();
            }

            if (!bringTargetIntoView)
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                var container = AlbumGrid.ItemContainerGenerator.ContainerFromItem(album) as FrameworkElement;
                if (container != null)
                    container.BringIntoView();
            }), DispatcherPriority.Background);
        }

        /// <summary>
        /// Returns the indices of AlbumGridItem entries whose containers are inside
        /// (or near) the ScrollViewer viewport.
        /// </summary>
        private List<int> GetVisibleIndices()
        {
            var result = new List<int>();
            if (AlbumScrollViewer == null || _albumItems.Count == 0)
                return result;

            if (AlbumScrollViewer.ViewportHeight <= 0)
                return result;

            // Estimate a narrow index window first, then verify with transforms.
            double tileStrideX = Math.Max(1, CurrentTileSize + Math.Max(0, TileMargin.Right));
            double tileStrideY = Math.Max(1, CurrentTileSize + Math.Max(0, TileMargin.Bottom) + (UILayoutConstants.AlbumTrackMetaRowHeight * TileScaleRatio));
            int perRow = Math.Max(1, (int)Math.Floor(Math.Max(1, AlbumScrollViewer.ViewportWidth) / tileStrideX));

            int startRow = Math.Max(0, (int)Math.Floor(AlbumScrollViewer.VerticalOffset / tileStrideY) - 2);
            int endRow = (int)Math.Ceiling((AlbumScrollViewer.VerticalOffset + AlbumScrollViewer.ViewportHeight) / tileStrideY) + 2;
            int startIndex = Math.Max(0, startRow * perRow);
            int endIndex = Math.Min(_albumItems.Count - 1, ((endRow + 1) * perRow) - 1);

            var gen = AlbumGrid.ItemContainerGenerator;
            for (int i = startIndex; i <= endIndex; i++)
            {
                if (_albumItems[i] is not AlbumGridItem) continue;

                var container = gen.ContainerFromIndex(i) as FrameworkElement;
                if (container == null)
                    continue;

                GeneralTransform transform;
                try { transform = container.TransformToAncestor(AlbumScrollViewer); }
                catch { continue; }

                var topLeft = transform.Transform(new Point(0, 0));
                double itemTop = topLeft.Y;
                double itemBottom = itemTop + container.ActualHeight;

                if (itemBottom >= 0 && itemTop <= AlbumScrollViewer.ViewportHeight)
                    result.Add(i);
            }

            return result;
        }

        /// <summary>
        /// Index range for viewport album-art load. Uses container intersection when available; otherwise estimates
        /// from wrap geometry so art loads before layout exposes <see cref="GetVisibleIndices"/> hits.
        /// </summary>
        private void GetAlbumArtViewportIndexRange(out int firstIdx, out int lastIdx)
        {
            firstIdx = 0;
            lastIdx = -1;
            if (_albumItems.Count == 0)
                return;

            var visible = GetVisibleIndices();
            if (visible.Count > 0)
            {
                firstIdx = Math.Max(0, visible[0] - UILayoutConstants.AlbumVisibleRangeOverscan);
                lastIdx = Math.Min(_albumItems.Count - 1, visible[^1] + UILayoutConstants.AlbumVisibleRangeOverscan);
                return;
            }

            if (AlbumScrollViewer == null)
                return;

            double vw = AlbumScrollViewer.ViewportWidth > 0
                ? AlbumScrollViewer.ViewportWidth
                : Math.Max(UILayoutConstants.AlbumWrapFallbackWidth, ActualWidth - UILayoutConstants.AlbumWrapHorizontalPadding);
            double vh = AlbumScrollViewer.ViewportHeight > 0
                ? AlbumScrollViewer.ViewportHeight
                : UILayoutConstants.AlbumArtBootstrapViewportHeightFallback;

            double tileStrideX = Math.Max(1, CurrentTileSize + Math.Max(0, TileMargin.Right));
            double tileStrideY = Math.Max(1, CurrentTileSize + Math.Max(0, TileMargin.Bottom) + (UILayoutConstants.AlbumTrackMetaRowHeight * TileScaleRatio));
            int perRow = Math.Max(1, (int)Math.Floor(Math.Max(1, vw) / tileStrideX));
            int rowCount = Math.Max(
                UILayoutConstants.AlbumArtViewportBootstrapMinRows,
                (int)Math.Ceiling(vh / tileStrideY) + UILayoutConstants.AlbumVisibleRangeOverscan);

            lastIdx = Math.Min(_albumItems.Count - 1, perRow * rowCount - 1);
        }

        private static int TryGetCount(IEnumerable? source)
        {
            if (source is ICollection col)
                return col.Count;
            return -1;
        }

        private void AlbumSizeSlider_DragStarted(object sender, DragStartedEventArgs e)
        {
            CloseAlbumDetail();
            _isDraggingSlider = true;
            _dragStartTileSize = CurrentTileSize;
            _dragTargetSize = CurrentTileSize;
            _dragScaleTransform = new ScaleTransform(1, 1);
            AlbumGrid.LayoutTransform = _dragScaleTransform;
        }

        private void AlbumSizeSlider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            _isDraggingSlider = false;

            double scrollRatio = 0;
            if (AlbumScrollViewer != null && AlbumScrollViewer.ExtentHeight > 0)
                scrollRatio = AlbumScrollViewer.VerticalOffset / AlbumScrollViewer.ExtentHeight;

            AlbumGrid.LayoutTransform = Transform.Identity;
            _dragScaleTransform = null;
            CurrentTileSize = _dragTargetSize;

            if (AlbumScrollViewer != null)
            {
                AlbumScrollViewer.UpdateLayout();
                AlbumScrollViewer.ScrollToVerticalOffset(scrollRatio * AlbumScrollViewer.ExtentHeight);
            }

            ScheduleArtLoad();
        }

        // --- Album list building ---

        private readonly record struct AlbumGridLayoutHint(
            double ViewportWidth,
            double ViewportHeight,
            double TileSize,
            double MarginRight,
            double MarginBottom,
            double TileScaleRatio);

        private AlbumGridLayoutHint CaptureAlbumGridLayoutHint()
        {
            double vw = 0, vh = 0;
            if (AlbumScrollViewer != null)
            {
                vw = AlbumScrollViewer.ViewportWidth;
                vh = AlbumScrollViewer.ViewportHeight;
            }
            if (vw <= 0)
                vw = Math.Max(1, ActualWidth - UILayoutConstants.AlbumWrapHorizontalPadding * 2);
            if (vh <= 0)
                vh = 400;
            return new AlbumGridLayoutHint(vw, vh, CurrentTileSize, TileMargin.Right, TileMargin.Bottom, TileScaleRatio);
        }

        private static void GetWrapPanelStride(in AlbumGridLayoutHint h, out double tileStrideX, out double tileStrideY)
        {
            tileStrideX = Math.Max(1, h.TileSize + Math.Max(0, h.MarginRight));
            tileStrideY = Math.Max(1, h.TileSize + Math.Max(0, h.MarginBottom) + UILayoutConstants.AlbumTrackMetaRowHeight * h.TileScaleRatio);
        }

        private static int ComputePrefixGoal(IReadOnlyList<AlbumGridItem> grouped, int? targetIndex, in AlbumGridLayoutHint hint)
        {
            if (grouped.Count == 0)
                return 0;
            GetWrapPanelStride(hint, out var sx, out var sy);
            double vw = hint.ViewportWidth > 0 ? hint.ViewportWidth : UILayoutConstants.AlbumWrapFallbackWidth;
            double vh = hint.ViewportHeight > 0 ? hint.ViewportHeight : 400;
            int perRow = Math.Max(1, (int)Math.Floor(Math.Max(1, vw) / sx));
            int visibleRows = Math.Max(1, (int)Math.Ceiling(Math.Max(1, vh) / sy));
            int overscanRows = UILayoutConstants.AlbumRebuildPrefixOverscanRows;
            int firstScreen = Math.Min(grouped.Count, (visibleRows + overscanRows) * perRow);
            if (!targetIndex.HasValue)
                return firstScreen;
            int extra = (visibleRows + overscanRows) * perRow;
            return Math.Min(grouped.Count, targetIndex.Value + 1 + extra);
        }

        private static int? FindTargetAlbumIndex(IReadOnlyList<AlbumGridItem> grouped, string albumName, string? artistName)
        {
            if (string.IsNullOrWhiteSpace(albumName))
                return null;
            if (!string.IsNullOrWhiteSpace(artistName))
            {
                for (int i = 0; i < grouped.Count; i++)
                {
                    var g = grouped[i];
                    if (string.Equals(g.AlbumTitle, albumName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(g.Artist, artistName, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }
            for (int i = 0; i < grouped.Count; i++)
            {
                if (string.Equals(grouped[i].AlbumTitle, albumName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return null;
        }

        private static bool TryGetAlbumGridGroupKey(Song track, out string albumTitle, out string artistKey)
        {
            albumTitle = "";
            artistKey = "";
            if (track == null)
                return false;
            var album = track.Album ?? string.Empty;
            if (string.IsNullOrWhiteSpace(album) || album == "Unknown Album")
                return false;

            albumTitle = album;
            artistKey = !string.IsNullOrWhiteSpace(track.AlbumArtist)
                ? track.AlbumArtist
                : track.Artist ?? string.Empty;
            return true;
        }

        private static List<AlbumGridItem> BuildGroupedAlbums(IEnumerable<Song> songsEnumerable, AlbumSortMode sortMode, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            List<Song> songs;
            try { songs = songsEnumerable.ToList(); }
            catch { return new List<AlbumGridItem>(); }

            var query = songs
                .Where(t => !string.IsNullOrWhiteSpace(t.Album) && t.Album != "Unknown Album")
                .GroupBy(t =>
                {
                    var albumArtist = !string.IsNullOrWhiteSpace(t.AlbumArtist)
                        ? t.AlbumArtist
                        : t.Artist ?? string.Empty;
                    return (Album: t.Album ?? string.Empty, Artist: albumArtist);
                })
                .Select(g =>
                {
                    var rep = g.First();
                    BitmapImage? art = null;
                    if (!string.IsNullOrEmpty(rep.ThumbnailCachePath))
                        art = AlbumArtCacheManager.LoadFromCachePath(rep.ThumbnailCachePath);
                    return new AlbumGridItem(g.Key.Album, g.Key.Artist, rep, art);
                });

            query = sortMode switch
            {
                AlbumSortMode.Artist => query
                    .OrderBy(a => a.Artist, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(a => a.AlbumTitle, StringComparer.OrdinalIgnoreCase),
                _ => query
                    .OrderBy(a => a.AlbumTitle, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(a => a.Artist, StringComparer.OrdinalIgnoreCase)
            };

            return query.ToList();
        }

        private (double? scrollRatio, double? scrollOffsetPixels, (string albumName, string? artistName, bool openDetails, string? selectedTrackFilePath)? mergedPending)
            CaptureRebuildRestoreStateAndCloseFlyout(bool preserveViewState)
        {
            double? scrollRatio = null;
            double? scrollOffsetPixels = null;
            (string albumName, string? artistName, bool openDetails, string? selectedTrackFilePath)? mergedPending = null;

            if (preserveViewState)
            {
                if (AlbumScrollViewer != null && AlbumScrollViewer.ExtentHeight > 0)
                {
                    scrollRatio = AlbumScrollViewer.VerticalOffset / AlbumScrollViewer.ExtentHeight;
                    scrollOffsetPixels = AlbumScrollViewer.VerticalOffset;
                }

                if (_pendingAlbumSelection.HasValue)
                    mergedPending = _pendingAlbumSelection;
                else if (_selectedAlbum != null && _currentFlyout != null)
                {
                    var path = SelectedFlyoutTrackFilePath;
                    mergedPending = (_selectedAlbum.AlbumTitle, _selectedAlbum.Artist, true, path);
                }
            }
            else if (_pendingAlbumSelection.HasValue)
                mergedPending = _pendingAlbumSelection;

            CloseAlbumDetail();
            return (scrollRatio, scrollOffsetPixels, mergedPending);
        }

        private async Task RebuildAlbumItemsAsync(bool preserveViewState = true)
        {
            (double? scrollRatio, double? scrollOffsetPixels, var mergedPending) = Dispatcher.CheckAccess()
                ? CaptureRebuildRestoreStateAndCloseFlyout(preserveViewState)
                : await Dispatcher.InvokeAsync(() => CaptureRebuildRestoreStateAndCloseFlyout(preserveViewState)).Task.ConfigureAwait(false);

            CancelAllAlbumArtWork();
            _rebuildCts?.Cancel();
            _rebuildCts?.Dispose();
            _rebuildCts = new CancellationTokenSource();
            var ct = _rebuildCts.Token;

            ShowLoadingIndicator();

            if (_itemsSource is not IEnumerable<Song> songsRef)
            {
                HideLoadingIndicator();
                return;
            }

            var sortMode = _sortMode;

            AlbumGridLayoutHint layoutHint;
            if (Dispatcher.CheckAccess())
                layoutHint = CaptureAlbumGridLayoutHint();
            else
                layoutHint = await Dispatcher.InvokeAsync(CaptureAlbumGridLayoutHint).Task.ConfigureAwait(false);

            List<AlbumGridItem> grouped;
            try
            {
                grouped = await Task.Run(() => BuildGroupedAlbums(songsRef, sortMode, ct), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                HideLoadingIndicator();
                return;
            }

            if (ct.IsCancellationRequested)
            {
                HideLoadingIndicator();
                return;
            }

            if (grouped.Count == 0)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    _albumItems.Clear();
                    HideLoadingIndicator();
                }, DispatcherPriority.Loaded).Task.ConfigureAwait(false);
                return;
            }

            int? targetIndex = null;
            if (mergedPending.HasValue)
            {
                var p = mergedPending.Value;
                targetIndex = FindTargetAlbumIndex(grouped, p.albumName, p.artistName);
            }

            int prefixGoal = ComputePrefixGoal(grouped, targetIndex, layoutHint);

            int i = 0;
            int batchLoopIndex = 0;
            int batchesSinceSample = int.MaxValue;
            SystemResourceSnapshot? lastResourceSnapshot = null;
            int scanConcurrencySmoothed = 0;

            while (i < grouped.Count && !ct.IsCancellationRequested)
            {
                bool inPrefixPhase = i < prefixGoal;
                bool mustSample = batchLoopIndex == 0 || batchesSinceSample >= UILayoutConstants.AlbumRebuildMetricsResampleEveryNBatches;
                if (mustSample)
                {
                    var sampleInterval = lastResourceSnapshot.HasValue && lastResourceSnapshot.Value.CpuBusyPercent < 40
                        ? TimeSpan.FromMilliseconds(50)
                        : TimeSpan.FromMilliseconds(100);
                    try
                    {
                        lastResourceSnapshot = await Task.Run(() => WindowsSystemMetrics.Sample(sampleInterval), ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        HideLoadingIndicator();
                        return;
                    }

                    ScanConcurrencyAdvisor.Recommend(
                        lastResourceSnapshot.Value,
                        Environment.ProcessorCount,
                        ref scanConcurrencySmoothed);
                    batchesSinceSample = 0;
                }
                else
                    batchesSinceSample++;

                int baseBatch = AlbumRebuildUiAdvisor.ItemsPerDispatcherBatch(scanConcurrencySmoothed);
                if (inPrefixPhase)
                    baseBatch = AlbumRebuildUiAdvisor.PrefixPhaseItemsPerBatch(baseBatch);

                int end = i + baseBatch;
                if (inPrefixPhase)
                    end = Math.Min(end, prefixGoal);
                end = Math.Min(end, grouped.Count);

                if (end <= i)
                    end = Math.Min(i + UILayoutConstants.AlbumRebuildBatchMin, grouped.Count);

                bool isFirstUiBatch = i == 0;
                var priority = isFirstUiBatch ? DispatcherPriority.Loaded : DispatcherPriority.Background;
                int endLocal = end;
                int iLocal = i;

                try
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (isFirstUiBatch)
                            _albumItems.Clear();
                        for (int j = iLocal; j < endLocal; j++)
                            _albumItems.Add(grouped[j]);

                        if (isFirstUiBatch && endLocal > iLocal)
                            _ = LoadVisibleAlbumArtAsync();
                    }, priority).Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    HideLoadingIndicator();
                    return;
                }

                i = endLocal;
                batchLoopIndex++;

                if (batchLoopIndex == 1)
                    _ = PrefetchAllAlbumArtBackgroundAsync();

                if (lastResourceSnapshot.HasValue && lastResourceSnapshot.Value.CpuBusyPercent >= 88 && !inPrefixPhase)
                {
                    try { await Task.Delay(5, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException)
                    {
                        HideLoadingIndicator();
                        return;
                    }
                }
                else
                    await Task.Yield();
            }

            if (!ct.IsCancellationRequested)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    AlbumGrid.UpdateLayout();
                    AlbumScrollViewer?.UpdateLayout();

                    if (mergedPending.HasValue)
                        ApplyMergedAlbumSelection(mergedPending.Value, bringTargetIntoView: false);

                    AlbumGrid.UpdateLayout();
                    AlbumScrollViewer?.UpdateLayout();

                    if (preserveViewState && AlbumScrollViewer != null)
                    {
                        double y;
                        if (scrollOffsetPixels.HasValue)
                            y = scrollOffsetPixels.Value;
                        else if (scrollRatio.HasValue)
                            y = scrollRatio.Value * AlbumScrollViewer.ExtentHeight;
                        else
                            y = double.NaN;

                        if (!double.IsNaN(y))
                        {
                            AlbumScrollViewer.ScrollToVerticalOffset(
                                Math.Min(Math.Max(0, y), AlbumScrollViewer.ScrollableHeight));
                        }
                    }

                    KickViewportAlbumArtNow();
                    HideLoadingIndicator();
                }, DispatcherPriority.Loaded).Task.ConfigureAwait(false);
            }
        }

        // --- Art loading ---

        private async Task PrefetchAllAlbumArtBackgroundAsync()
        {
            _prefetchArtCts?.Cancel();
            _prefetchArtCts?.Dispose();
            _prefetchArtCts = new CancellationTokenSource();
            var ct = _prefetchArtCts.Token;

            int batchesSinceSample = int.MaxValue;
            SystemResourceSnapshot? lastResourceSnapshot = null;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int countSnapshot;
                    try
                    {
                        countSnapshot = await Dispatcher.InvokeAsync(
                            () => _albumItems.Count,
                            DispatcherPriority.Loaded).Task.ConfigureAwait(false);
                    }
                    catch (TaskCanceledException) { return; }

                    if (countSnapshot == 0)
                        break;

                    int indexChunk = UILayoutConstants.AlbumArtPrefetchIndexChunk;
                    if (lastResourceSnapshot.HasValue && lastResourceSnapshot.Value.CpuBusyPercent >= 88)
                        indexChunk = Math.Max(8, indexChunk / 2);

                    bool anyWorkThisPass = false;

                    for (int start = 0; start < countSnapshot && !ct.IsCancellationRequested; start += indexChunk)
                    {
                        try
                        {
                            int liveCount = await Dispatcher.InvokeAsync(() => _albumItems.Count, DispatcherPriority.Loaded).Task.ConfigureAwait(false);
                            if (liveCount > countSnapshot)
                                countSnapshot = liveCount;
                        }
                        catch (TaskCanceledException) { return; }

                        bool mustSample = batchesSinceSample >= UILayoutConstants.AlbumArtPrefetchMetricsResampleEveryNBatches;
                        if (mustSample)
                        {
                            var sampleInterval = lastResourceSnapshot.HasValue && lastResourceSnapshot.Value.CpuBusyPercent < 40
                                ? TimeSpan.FromMilliseconds(50)
                                : TimeSpan.FromMilliseconds(100);
                            try
                            {
                                lastResourceSnapshot = await Task.Run(() => WindowsSystemMetrics.Sample(sampleInterval), ct).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) { return; }
                            batchesSinceSample = 0;
                            indexChunk = UILayoutConstants.AlbumArtPrefetchIndexChunk;
                            if (lastResourceSnapshot.HasValue && lastResourceSnapshot.Value.CpuBusyPercent >= 88)
                                indexChunk = Math.Max(8, indexChunk / 2);
                        }
                        else
                            batchesSinceSample++;

                        int s = start;
                        int e = Math.Min(start + indexChunk, countSnapshot);

                        List<AlbumGridItem> batch;
                        try
                        {
                            batch = await Dispatcher.InvokeAsync(() =>
                            {
                                var list = new List<AlbumGridItem>();
                                for (int i = s; i < e; i++)
                                {
                                    if (_albumItems[i] is AlbumGridItem a && a.AlbumArtSource == null)
                                        list.Add(a);
                                }
                                return list;
                            }, DispatcherPriority.Loaded).Task.ConfigureAwait(false);
                        }
                        catch (TaskCanceledException) { return; }

                        if (batch.Count > 0)
                        {
                            anyWorkThisPass = true;
                            try { await LoadAlbumArtForItemsAsync(batch, ct, backgroundFriendly: true).ConfigureAwait(false); }
                            catch (OperationCanceledException) { return; }
                        }

                        if (lastResourceSnapshot.HasValue && lastResourceSnapshot.Value.CpuBusyPercent >= 88)
                        {
                            try { await Task.Delay(5, ct).ConfigureAwait(false); }
                            catch (OperationCanceledException) { return; }
                        }
                        else
                            await Task.Yield();
                    }

                    bool anyMissing;
                    try
                    {
                        anyMissing = await Dispatcher.InvokeAsync(() =>
                        {
                            for (int i = 0; i < _albumItems.Count; i++)
                            {
                                if (_albumItems[i] is AlbumGridItem a && a.AlbumArtSource == null)
                                    return true;
                            }
                            return false;
                        }, DispatcherPriority.Loaded).Task.ConfigureAwait(false);
                    }
                    catch (TaskCanceledException) { return; }

                    if (!anyMissing)
                        break;
                    if (!anyWorkThisPass)
                        break;
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task LoadVisibleAlbumArtAsync()
        {
            if (!Dispatcher.CheckAccess())
            {
                KickViewportAlbumArtNow();
                return;
            }

            if (AlbumScrollViewer == null || _albumItems.Count == 0) return;

            GetAlbumArtViewportIndexRange(out int firstIdx, out int lastIdx);
            if (lastIdx < firstIdx)
                return;

            _viewportArtCts?.Cancel();
            _viewportArtCts?.Dispose();
            _viewportArtCts = new CancellationTokenSource();
            var ct = _viewportArtCts.Token;

            var itemsToLoad = new List<AlbumGridItem>();
            for (int i = firstIdx; i <= lastIdx; i++)
            {
                if (_albumItems[i] is AlbumGridItem a && a.AlbumArtSource == null)
                    itemsToLoad.Add(a);
            }

            try { await LoadAlbumArtForItemsAsync(itemsToLoad, ct, backgroundFriendly: false); }
            catch (OperationCanceledException) { }
        }

        private async Task LoadAlbumArtForItemsAsync(IReadOnlyCollection<AlbumGridItem> items, CancellationToken ct, bool backgroundFriendly)
        {
            if (items.Count == 0)
                return;

            double tileDp = CurrentTileSize;
            if (!Dispatcher.CheckAccess())
            {
                try
                {
                    tileDp = await Dispatcher.InvokeAsync(() => CurrentTileSize, DispatcherPriority.Loaded).Task.ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }
            int targetSize = Math.Max(UILayoutConstants.AlbumArtMinimumTargetSize, (int)Math.Round(tileDp));
            int parallel = 4;
            try
            {
                parallel = await Task.Run(() =>
                {
                    int smoothed = 0;
                    var snap = WindowsSystemMetrics.Sample(TimeSpan.FromMilliseconds(50));
                    return ScanConcurrencyAdvisor.Recommend(snap, Environment.ProcessorCount, ref smoothed);
                }, ct).ConfigureAwait(false);
                int maxP = backgroundFriendly ? UILayoutConstants.AlbumArtPrefetchMaxParallelism : UILayoutConstants.AlbumArtLoadMaxParallelism;
                parallel = Math.Clamp(parallel, 1, maxP);
                if (backgroundFriendly)
                {
                    parallel = Math.Max(1, parallel / 2);
                    parallel = Math.Min(parallel, UILayoutConstants.AlbumArtPrefetchMaxParallelism);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                int maxP = backgroundFriendly ? UILayoutConstants.AlbumArtPrefetchMaxParallelism : UILayoutConstants.AlbumArtLoadMaxParallelism;
                parallel = Math.Clamp(4, 1, maxP);
                if (backgroundFriendly)
                {
                    parallel = Math.Max(1, parallel / 2);
                    parallel = Math.Min(parallel, UILayoutConstants.AlbumArtPrefetchMaxParallelism);
                }
            }

            var assignPriority = DispatcherPriority.Normal;
            using var throttler = new SemaphoreSlim(parallel);
            var tasks = items.Select(async item =>
            {
                if (item.AlbumArtSource != null || ct.IsCancellationRequested)
                    return;

                await throttler.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var img = AlbumArtThumbnailHelper.LoadForTrack(item.RepresentativeTrack, targetSize);
                    if (img == null || ct.IsCancellationRequested)
                        return;

                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (!ct.IsCancellationRequested && item.AlbumArtSource == null)
                            item.AlbumArtSource = img;
                    }, assignPriority);
                }
                finally
                {
                    throttler.Release();
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private void AlbumScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange == 0 && e.ExtentHeightChange == 0)
                return;

            ScheduleArtLoad();
        }

        private void AlbumItem_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is AlbumGridItem item)
            {
                AlbumClicked?.Invoke(this, item);

                if (_selectedAlbum == item)
                    CloseAlbumDetail();
                else
                {
                    // Selecting a new album clears any prior flyout track selection highlight.
                    SelectedFlyoutTrackFilePath = null;
                    ShowAlbumDetail(item);
                }
            }
        }

        private void SortModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            _sortMode = SortModeCombo.SelectedIndex == 1 ? AlbumSortMode.Artist : AlbumSortMode.Album;
            _ = RebuildAlbumItemsAsync(preserveViewState: false);
        }

        // --- Album detail flyout ---

        private void PopulateFlyoutTrackLists(AlbumFlyoutItem flyout, string albumTitle, string albumArtistKey)
        {
            if (_itemsSource is not IEnumerable<Song> songs)
                return;

            flyout.Tracks = AlbumTrackOrder.SortByAlbumSequence(
                songs.Where(s =>
                    string.Equals(s.Album, albumTitle, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        !string.IsNullOrWhiteSpace(s.AlbumArtist) ? s.AlbumArtist : s.Artist,
                        albumArtistKey,
                        StringComparison.OrdinalIgnoreCase)));

            int half = (int)Math.Ceiling(flyout.Tracks.Count / 2.0);
            flyout.TracksColumn1 = flyout.Tracks.Take(half).ToList();
            flyout.TracksColumn2 = flyout.Tracks.Skip(half).ToList();
            flyout.AlbumMetadata = BuildAlbumMetadata(flyout.Tracks);
            FlyoutPanelHeight = CalculateFlyoutHeight(flyout.Tracks.Count);
        }

        private void PatchOpenFlyoutForGroup(string albumTitle, string albumArtistKey)
        {
            if (_currentFlyout == null)
                return;

            var flyout = _currentFlyout;
            if (_selectedAlbum?.RepresentativeTrack is Song repMeta)
            {
                flyout.Genre = repMeta.Genre ?? "";
                flyout.Year = repMeta.Year > 0 ? repMeta.Year.ToString() : "";
            }

            flyout.AlbumArtSource = _selectedAlbum?.AlbumArtSource;

            PopulateFlyoutTrackLists(flyout, albumTitle, albumArtistKey);
            FlyoutPanelWidth = GetWrapPanelWidth();
            if (_selectedAlbum != null)
                UpdateFlyoutArrowOffset(_selectedAlbum);

            if (_selectedAlbum?.RepresentativeTrack is Song repForFull)
            {
                var flyoutRef = flyout;
                _ = Task.Run(() =>
                {
                    var full = AlbumArtThumbnailHelper.LoadFullSizeForTrack(repForFull);
                    if (full == null)
                        return;
                    Dispatcher.InvokeAsync(() =>
                    {
                        if (ReferenceEquals(_currentFlyout, flyoutRef))
                            flyoutRef.AlbumArtSource = full;
                    });
                });
            }
        }

        private void ShowAlbumDetail(AlbumGridItem album, bool bringFlyoutIntoView = true)
        {
            if (_currentFlyout != null)
            {
                _albumItems.Remove(_currentFlyout);
                _currentFlyout = null;
                AlbumGrid.UpdateLayout();
            }

            _selectedAlbum = album;

            var flyout = new AlbumFlyoutItem
            {
                AlbumTitle = album.AlbumTitle,
                Artist = album.Artist,
                Genre = album.RepresentativeTrack?.Genre ?? "",
                Year = album.RepresentativeTrack?.Year > 0 ? album.RepresentativeTrack.Year.ToString() : "",
                AlbumArtSource = album.AlbumArtSource
            };

            if (album.AlbumArtSource == null && album.RepresentativeTrack is Song repForThumb)
            {
                _ = Task.Run(() =>
                {
                    var img = AlbumArtThumbnailHelper.LoadForTrack(repForThumb);
                    if (img != null)
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            album.AlbumArtSource = img;
                            if (_selectedAlbum == album)
                                flyout.AlbumArtSource = img;
                        });
                    }
                });
            }

            // Flyout should display full-size album art; load it asynchronously so opening stays instant.
            if (album.RepresentativeTrack is Song repForFull)
            {
                _ = Task.Run(() =>
                {
                    var full = AlbumArtThumbnailHelper.LoadFullSizeForTrack(repForFull);
                    if (full != null)
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            if (_selectedAlbum == album && _currentFlyout == flyout)
                                flyout.AlbumArtSource = full;
                        });
                    }
                });
            }

            PopulateFlyoutTrackLists(flyout, album.AlbumTitle, album.Artist);

            int albumIndex = _albumItems.IndexOf(album);
            int insertIndex = GetRowEndIndex(albumIndex);

            FlyoutPanelWidth = GetWrapPanelWidth();
            UpdateFlyoutArrowOffset(album);

            _currentFlyout = flyout;
            _albumItems.Insert(insertIndex, flyout);

            if (!bringFlyoutIntoView)
                return;

            Dispatcher.InvokeAsync(() =>
            {
                var container = AlbumGrid.ItemContainerGenerator.ContainerFromItem(flyout) as FrameworkElement;
                container?.BringIntoView();
            }, DispatcherPriority.Background);
        }

        private void CloseAlbumDetail()
        {
            _selectedAlbum = null;
            FlyoutPanelHeight = FlyoutHeight;
            if (_currentFlyout != null)
            {
                _albumItems.Remove(_currentFlyout);
                _currentFlyout = null;
            }

            SelectedFlyoutTrackFilePath = null;
        }

        private void AlbumsView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            TryCaptureResizeAnchor();
            _resizeAnchorDebounce.Stop();
            _resizeAnchorDebounce.Start();

            ScheduleArtLoad();
            if (_currentFlyout == null || _selectedAlbum == null)
                return;

            // Keep width in sync while the window is actively resizing.
            FlyoutPanelWidth = GetWrapPanelWidth();
            _flyoutResizeDebounce.Stop();
            _flyoutResizeDebounce.Start();
        }

        private bool TryCaptureResizeAnchor()
        {
            if (AlbumScrollViewer == null)
                return false;

            if (_selectedAlbum != null)
            {
                if (!TryGetAlbumItemTopInViewport(_selectedAlbum, out var itemTop))
                    itemTop = 12;

                _pendingResizeAnchor = new ResizeAnchorState(
                    _selectedAlbum.AlbumTitle,
                    _selectedAlbum.Artist,
                    itemTop,
                    ResizeAnchorKind.SelectedAlbum);
                return true;
            }

            if (!TryGetFirstVisibleAlbumItem(out var visibleAlbum, out var visibleTop) || visibleAlbum == null)
                return false;

            _pendingResizeAnchor = new ResizeAnchorState(
                visibleAlbum.AlbumTitle,
                visibleAlbum.Artist,
                visibleTop,
                ResizeAnchorKind.FirstVisibleAlbum);
            return true;
        }

        private bool TryGetFirstVisibleAlbumItem(out AlbumGridItem? album, out double itemTop)
        {
            album = null;
            itemTop = 0;

            if (AlbumScrollViewer == null || _albumItems.Count == 0)
                return false;

            var visibleIndices = GetVisibleIndices();
            if (visibleIndices.Count == 0)
                return false;

            double bestTop = double.MaxValue;
            foreach (var index in visibleIndices)
            {
                if (index < 0 || index >= _albumItems.Count)
                    continue;

                if (_albumItems[index] is not AlbumGridItem candidate)
                    continue;

                if (!TryGetAlbumItemTopInViewport(candidate, out var top))
                    continue;

                if (top < bestTop)
                {
                    bestTop = top;
                    album = candidate;
                    itemTop = top;
                }
            }

            return album != null;
        }

        private bool TryGetAlbumItemTopInViewport(AlbumGridItem album, out double itemTop)
        {
            itemTop = 0;
            if (AlbumScrollViewer == null)
                return false;

            var container = AlbumGrid.ItemContainerGenerator.ContainerFromItem(album) as FrameworkElement;
            if (container == null)
                return false;

            try
            {
                var transform = container.TransformToAncestor(AlbumScrollViewer);
                itemTop = transform.Transform(new Point(0, 0)).Y;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void RestoreResizeAnchorIfPending()
        {
            if (!_pendingResizeAnchor.HasValue || AlbumScrollViewer == null || _albumItems.Count == 0)
                return;

            var pending = _pendingResizeAnchor.Value;
            var anchor = FindAlbumItem(pending.AlbumTitle, pending.Artist);
            if (anchor == null)
            {
                _pendingResizeAnchor = null;
                return;
            }

            AlbumGrid.UpdateLayout();
            AlbumScrollViewer.UpdateLayout();

            if (!TryGetAlbumItemTopInViewport(anchor, out var currentTop))
            {
                _pendingResizeAnchor = null;
                return;
            }

            double desiredTop = pending.OffsetFromViewportTop;
            if (pending.Kind == ResizeAnchorKind.SelectedAlbum)
                desiredTop = Math.Min(Math.Max(0, desiredTop), Math.Max(0, AlbumScrollViewer.ViewportHeight - CurrentTileSize));
            else
                desiredTop = Math.Max(0, desiredTop);

            double targetOffset = AlbumScrollViewer.VerticalOffset + (currentTop - desiredTop);
            targetOffset = Math.Min(Math.Max(0, targetOffset), AlbumScrollViewer.ScrollableHeight);
            AlbumScrollViewer.ScrollToVerticalOffset(targetOffset);
            _pendingResizeAnchor = null;
        }

        private void RefreshOpenFlyoutLayout()
        {
            if (_isRefreshingFlyoutLayout || _currentFlyout == null || _selectedAlbum == null)
                return;

            if (!_albumItems.Contains(_selectedAlbum))
            {
                CloseAlbumDetail();
                return;
            }

            _isRefreshingFlyoutLayout = true;
            try
            {
                double preservedOffset = AlbumScrollViewer?.VerticalOffset ?? 0;

                int existingFlyoutIndex = _albumItems.IndexOf(_currentFlyout);
                if (existingFlyoutIndex >= 0)
                    _albumItems.RemoveAt(existingFlyoutIndex);

                AlbumGrid.UpdateLayout();

                int albumIndex = _albumItems.IndexOf(_selectedAlbum);
                if (albumIndex < 0)
                {
                    _currentFlyout = null;
                    _selectedAlbum = null;
                    return;
                }

                int insertIndex = GetRowEndIndex(albumIndex);
                _albumItems.Insert(insertIndex, _currentFlyout);

                FlyoutPanelWidth = GetWrapPanelWidth();
                FlyoutPanelHeight = CalculateFlyoutHeight(_currentFlyout.Tracks.Count);
                UpdateFlyoutArrowOffset(_selectedAlbum);

                if (AlbumScrollViewer != null)
                    AlbumScrollViewer.ScrollToVerticalOffset(Math.Min(preservedOffset, AlbumScrollViewer.ScrollableHeight));
            }
            finally
            {
                _isRefreshingFlyoutLayout = false;
            }
        }

        private static double CalculateFlyoutHeight(int trackCount)
        {
            int rowsPerColumn = (int)Math.Ceiling(Math.Max(0, trackCount) / 2.0);
            if (rowsPerColumn <= BaseFlyoutRowsPerColumn)
                return FlyoutHeight;

            int overflowRows = rowsPerColumn - BaseFlyoutRowsPerColumn;
            return FlyoutHeight + (overflowRows * FlyoutTrackRowHeight);
        }

        private static string BuildAlbumMetadata(IReadOnlyCollection<Song> tracks)
        {
            int trackCount = tracks.Count;
            double totalSeconds = tracks.Sum(t =>
            {
                if (t.DurationTimeSpan > TimeSpan.Zero)
                    return t.DurationTimeSpan.TotalSeconds;

                if (string.IsNullOrWhiteSpace(t.Duration))
                    return 0d;

                return TimeSpan.TryParseExact(t.Duration, @"mm\:ss", null, out var parsed)
                    ? parsed.TotalSeconds
                    : 0d;
            });

            TimeSpan totalDuration = TimeSpan.FromSeconds(totalSeconds);
            string songLabel = trackCount == 1 ? "song" : "songs";
            return $"{trackCount} {songLabel}, {FormatAlbumDuration(totalDuration)}";
        }

        private static string FormatAlbumDuration(TimeSpan duration)
        {
            int totalMinutes = (int)Math.Floor(duration.TotalMinutes);
            if (totalMinutes <= 0)
                return "0 minutes";

            int hours = totalMinutes / 60;
            int minutes = totalMinutes % 60;

            if (hours <= 0)
                return minutes == 1 ? "1 minute" : $"{minutes} minutes";

            if (minutes <= 0)
                return hours == 1 ? "1 hour" : $"{hours} hours";

            string hourPart = hours == 1 ? "1 hour" : $"{hours} hours";
            string minutePart = minutes == 1 ? "1 minute" : $"{minutes} minutes";
            return $"{hourPart}, {minutePart}";
        }

        private void FlyoutTrack_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is Song track)
            {
                // Single click selects (highlight), double click plays.
                SelectedFlyoutTrackFilePath = string.IsNullOrWhiteSpace(track.FilePath) ? null : track.FilePath;

                if (e.ClickCount == 2)
                {
                    PlayTrackRequested?.Invoke(this, track);
                    e.Handled = true;
                }
            }
        }

        private void FlyoutArtist_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_currentFlyout != null && !string.IsNullOrWhiteSpace(_currentFlyout.Artist))
                ArtistNavigationRequested?.Invoke(this, _currentFlyout.Artist);
        }

        private void FlyoutGenre_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_currentFlyout != null && !string.IsNullOrWhiteSpace(_currentFlyout.Genre))
                GenreNavigationRequested?.Invoke(this, _currentFlyout.Genre);
        }

        private void AlbumTrackContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            _contextMenuSong = null;
            if (sender is not ContextMenu menu || menu.PlacementTarget is not FrameworkElement target)
                return;

            if (target.DataContext is Song track)
            {
                _contextMenuSong = track;
                SelectedFlyoutTrackFilePath = string.IsNullOrWhiteSpace(track.FilePath) ? null : track.FilePath;
            }

            var addToPlaylistItem = FindMenuItemByHeader(menu.Items, "Add to Playlist");
            var mainWindow = Application.Current?.MainWindow as MainWindow;
            var playlists = mainWindow?.Playlists;
            if (addToPlaylistItem != null && playlists != null)
            {
                while (addToPlaylistItem.Items.Count > 2)
                    addToPlaylistItem.Items.RemoveAt(2);
                foreach (var playlist in playlists)
                {
                    var mi = new MenuItem { Header = playlist.Name, Tag = playlist };
                    mi.Click += AlbumContextMenu_PlaylistSubmenuClick;
                    addToPlaylistItem.Items.Add(mi);
                }
            }

            var showInQueueItem = FindMenuItemByHeader(menu.Items, "Show in Queue");
            if (showInQueueItem != null)
            {
                bool isInQueue = _contextMenuSong != null && mainWindow?.IsTrackInQueue(_contextMenuSong) == true;
                showInQueueItem.Visibility = isInQueue ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private static MenuItem? FindMenuItemByHeader(ItemCollection items, string header)
        {
            foreach (var item in items)
                if (item is MenuItem mi && mi.Header?.ToString() == header)
                    return mi;
            return null;
        }

        private void AlbumContextMenu_PlaylistSubmenuClick(object sender, RoutedEventArgs e)
        {
            if (_contextMenuSong == null || sender is not MenuItem mi || mi.Tag is not Playlist playlist)
                return;
            AddTrackToPlaylistRequested?.Invoke(this, (_contextMenuSong, playlist));
        }

        private void AlbumContextMenu_PlayNextClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) PlayNextRequested?.Invoke(this, _contextMenuSong); }
        private void AlbumContextMenu_AddToQueueClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) AddToQueueRequested?.Invoke(this, _contextMenuSong); }
        private void AlbumContextMenu_NewPlaylistClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) CreateNewPlaylistWithTrackRequested?.Invoke(this, _contextMenuSong); }
        private void AlbumContextMenu_ShowInArtistsClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) ShowInArtistsRequested?.Invoke(this, _contextMenuSong); }
        private void AlbumContextMenu_ShowInAlbumsClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) ShowInAlbumsRequested?.Invoke(this, _contextMenuSong); }
        private void AlbumContextMenu_ShowInSongsClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) ShowInSongsRequested?.Invoke(this, _contextMenuSong); }
        private void AlbumContextMenu_ShowInQueueClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) ShowInQueueRequested?.Invoke(this, _contextMenuSong); }
        private void AlbumContextMenu_InfoClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) InfoRequested?.Invoke(this, _contextMenuSong); }
        private void AlbumContextMenu_ShowInExplorerClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) ShowInExplorerRequested?.Invoke(this, _contextMenuSong); }
        private void AlbumContextMenu_RemoveFromLibraryClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) RemoveFromLibraryRequested?.Invoke(this, _contextMenuSong); }
        private void AlbumContextMenu_DeleteClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) DeleteRequested?.Invoke(this, _contextMenuSong); }

        /// <summary>
        /// Finds the collection index just past the last album on the same visual row
        /// as the item at <paramref name="albumIndex"/>.
        /// </summary>
        private int GetRowEndIndex(int albumIndex)
        {
            var gen = AlbumGrid.ItemContainerGenerator;
            var clickedContainer = gen.ContainerFromIndex(albumIndex) as FrameworkElement;
            if (clickedContainer == null) return albumIndex + 1;

            GeneralTransform clickedTransform;
            try { clickedTransform = clickedContainer.TransformToAncestor(AlbumScrollViewer); }
            catch { return albumIndex + 1; }

            double clickedY = clickedTransform.Transform(new Point(0, 0)).Y;

            int lastOnRow = albumIndex;
            for (int i = albumIndex + 1; i < _albumItems.Count; i++)
            {
                if (_albumItems[i] is not AlbumGridItem) break;

                var container = gen.ContainerFromIndex(i) as FrameworkElement;
                if (container == null) break;

                GeneralTransform transform;
                try { transform = container.TransformToAncestor(AlbumScrollViewer); }
                catch { break; }

                if (Math.Abs(transform.Transform(new Point(0, 0)).Y - clickedY) > 5) break;
                lastOnRow = i;
            }

            return lastOnRow + 1;
        }

        private double GetWrapPanelWidth()
        {
            var wrapPanel = FindVisualChild<WrapPanel>(AlbumGrid);
            return wrapPanel?.ActualWidth ?? Math.Max(UILayoutConstants.AlbumWrapFallbackWidth, ActualWidth - UILayoutConstants.AlbumWrapHorizontalPadding);
        }

        private void UpdateFlyoutArrowOffset(AlbumGridItem album)
        {
            var wrapPanel = FindVisualChild<WrapPanel>(AlbumGrid);
            var albumContainer = AlbumGrid.ItemContainerGenerator.ContainerFromItem(album) as FrameworkElement;
            if (wrapPanel == null || albumContainer == null)
            {
                FlyoutArrowOffset = new Thickness(0);
                return;
            }

            try
            {
                var pos = albumContainer.TransformToAncestor(wrapPanel).Transform(new Point(0, 0));
                double arrowX = pos.X + CurrentTileSize / 2 - 10;
                FlyoutArrowOffset = new Thickness(arrowX, 0, 0, 0);
            }
            catch
            {
                FlyoutArrowOffset = new Thickness(0);
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found) return found;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}
