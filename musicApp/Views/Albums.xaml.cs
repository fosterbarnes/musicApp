using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using musicApp;
using musicApp.Constants;

namespace musicApp.Views
{
    public enum AlbumsBrowseMode
    {
        AllAlbums,
        RecentlyAdded
    }

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

        public static readonly DependencyProperty BrowseModeProperty =
            DependencyProperty.Register(
                nameof(BrowseMode),
                typeof(AlbumsBrowseMode),
                typeof(AlbumsView),
                new PropertyMetadata(AlbumsBrowseMode.AllAlbums, OnBrowseModePropertyChanged));

        public AlbumsBrowseMode BrowseMode
        {
            get => (AlbumsBrowseMode)GetValue(BrowseModeProperty);
            set => SetValue(BrowseModeProperty, value);
        }

        public static readonly DependencyProperty SectionHeaderWidthProperty =
            DependencyProperty.Register(
                nameof(SectionHeaderWidth),
                typeof(double),
                typeof(AlbumsView),
                new PropertyMetadata(400d));

        public double SectionHeaderWidth
        {
            get => (double)GetValue(SectionHeaderWidthProperty);
            set => SetValue(SectionHeaderWidthProperty, value);
        }

        private static void OnBrowseModePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AlbumsView view)
            {
                view.UpdateSortBarVisibility();
                if (view.IsLoaded)
                    _ = view.RebuildAlbumItemsAsync(preserveViewState: true);
            }
        }

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
        private AlbumsBrowseMode _lastGridBuildBrowseMode = AlbumsBrowseMode.AllAlbums;

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

            Loaded += (_, __) =>
            {
                UpdateSortBarVisibility();
                SyncSectionHeaderWidth();
                KickViewportAlbumArtNow();
            };
            IsVisibleChanged += (_, __) =>
            {
                if (IsVisible)
                    KickViewportAlbumArtNow();
            };
            Unloaded += AlbumsView_OnUnloaded;
            SizeChanged += AlbumsView_SizeChanged;
        }

        internal void SyncSectionHeaderWidth()
        {
            if (AlbumScrollViewer == null)
                return;
            double w = AlbumScrollViewer.ViewportWidth;
            if (w <= 0)
                w = Math.Max(1, ActualWidth - UILayoutConstants.AlbumWrapHorizontalPadding);
            double pad = AlbumScrollViewer.Padding.Left + AlbumScrollViewer.Padding.Right;
            SectionHeaderWidth = Math.Max(100, w - pad);
        }

        private void UpdateSortBarVisibility()
        {
            if (AlbumSortControlsPanel == null)
                return;
            AlbumSortControlsPanel.Visibility = BrowseMode == AlbumsBrowseMode.AllAlbums
                ? Visibility.Visible
                : Visibility.Collapsed;
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
            }
        }
    }
}
