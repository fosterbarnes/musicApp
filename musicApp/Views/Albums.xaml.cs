using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using musicApp;
using musicApp.Helpers;

namespace musicApp.Views
{
    public enum AlbumsBrowseMode
    {
        AllAlbums,
        RecentlyAdded
    }

    public enum AlbumsGridRebuildPhase
    {
        Grouping,
        LoadingArtwork,
        Complete,
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

        // CurrentTileSize, TileMargin and RowWidth are attached and inherit down the visual tree (WPF inherits
        // only attached properties reliably). Tiles bind to their own inherited value, which exists at their
        // first measure; a RelativeSource AncestorType binding resolved only later, so new rows first
        // measured 41 px tall and the panel built ~5x too many.
        public double CurrentTileSize
        {
            get => (double)GetValue(CurrentTileSizeProperty);
            set => SetValue(CurrentTileSizeProperty, value);
        }

        public static readonly DependencyProperty CurrentTileSizeProperty =
            DependencyProperty.RegisterAttached(
                nameof(CurrentTileSize),
                typeof(double),
                typeof(AlbumsView),
                new FrameworkPropertyMetadata(DefaultTileSize, FrameworkPropertyMetadataOptions.Inherits, OnCurrentTileSizeChanged));

        public Thickness TileMargin
        {
            get => (Thickness)GetValue(TileMarginProperty);
            set => SetValue(TileMarginProperty, value);
        }

        public static readonly DependencyProperty TileMarginProperty =
            DependencyProperty.RegisterAttached(
                nameof(TileMargin),
                typeof(Thickness),
                typeof(AlbumsView),
                new FrameworkPropertyMetadata(new Thickness(0, 0, BaseTileRightGap, BaseTileBottomGap), FrameworkPropertyMetadataOptions.Inherits));

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

        /// <summary>Width of a tile row: tiles-per-row strides (Albums, centered) or the full header width (Recently Added).</summary>
        public double RowWidth
        {
            get => (double)GetValue(RowWidthProperty);
            set => SetValue(RowWidthProperty, value);
        }

        public static readonly DependencyProperty RowWidthProperty =
            DependencyProperty.RegisterAttached(
                nameof(RowWidth),
                typeof(double),
                typeof(AlbumsView),
                new FrameworkPropertyMetadata(600d, FrameworkPropertyMetadataOptions.Inherits));

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

        public static readonly DependencyProperty FlyoutSelectionRevisionProperty =
            DependencyProperty.Register(
                nameof(FlyoutSelectionRevision),
                typeof(int),
                typeof(AlbumsView),
                new PropertyMetadata(0));

        public int FlyoutSelectionRevision => (int)GetValue(FlyoutSelectionRevisionProperty);

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
                if (view.IsLoaded && !view._suppressBrowseModeRebuild)
                    view.ScheduleGridRebuild();
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
                view.OnTileSizeChanged();
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
                // Slider click or arrow keys: keep the same share of the list in view, like a drag.
                double ratio = view.GetScrollRatio();
                view.CurrentTileSize = newSize;
                view.RestoreScrollRatio(ratio);
            }
        }

        private enum AlbumSortMode { Album, Artist }

        private AlbumSortMode _sortMode = AlbumSortMode.Album;
        private IEnumerable? _itemsSource;
        private int _itemsSourceCount = -1;
        // The built grid in display order: AlbumGridItem tiles and (Recently Added) AlbumSectionHeaderItem headers.
        private List<object> _entries = new();
        private List<AlbumGridItem> _albums = new();
        // What AlbumGrid shows: _entries chunked into AlbumTileRow rows, plus the open AlbumFlyoutItem.
        private readonly ObservableCollection<object> _rows = new();
        private int _tilesPerRow;
        private CancellationTokenSource? _rebuildCts;
        private Song? _contextMenuSong;

        private (string albumName, string? artistName, bool openDetails, string? selectedTrackFilePath)? _pendingAlbumSelection;
        private readonly DispatcherTimer _collectionChangeRebuildDebounce;

        private readonly HashSet<string> _flyoutSelectedKeys = new(StringComparer.OrdinalIgnoreCase);
        private int _flyoutAnchorIndex = -1;

        private AlbumGridItem? _selectedAlbum;
        private AlbumFlyoutItem? _currentFlyout;

        // Template parts of AlbumGrid (Albums.xaml), found once in the constructor.
        private readonly ScrollViewer _albumScrollViewer;
        private readonly ItemsPresenter _itemsPresenter;
        private readonly FrameworkElement _rowSpace; // the ScrollContentPresenter: viewport minus padding

        private bool _isDraggingSlider;
        private double _dragStartTileSize;
        private double _dragTargetSize;
        private ScaleTransform? _dragScaleTransform;
        // Mode the grid holds or is being built for; null after a canceled build left it partial.
        private AlbumsBrowseMode? _lastGridBuildBrowseMode;
        private bool _isRebuilding;
        private int _rebuildGeneration;
        private bool _suppressBrowseModeRebuild;

        public Action<AlbumsGridRebuildPhase, int, int, int>? AlbumGridRebuildStatus { get; set; }

        public AlbumsView()
        {
            InitializeComponent();
            AlbumGrid.ApplyTemplate();
            _albumScrollViewer = AlbumGrid.Template.FindName("PART_ScrollViewer", AlbumGrid) as ScrollViewer
                ?? throw new InvalidOperationException("AlbumGrid template has no PART_ScrollViewer");
            _albumScrollViewer.ApplyTemplate();
            _itemsPresenter = AlbumGrid.Template.FindName("PART_ItemsPresenter", AlbumGrid) as ItemsPresenter
                ?? throw new InvalidOperationException("AlbumGrid template has no PART_ItemsPresenter");
            _rowSpace = _albumScrollViewer.Template.FindName("PART_ScrollContentPresenter", _albumScrollViewer) as FrameworkElement
                ?? throw new InvalidOperationException("ScrollViewer template has no PART_ScrollContentPresenter");
            // The row width follows the space next to the scrollbar, so re-chunk when it changes. Read from the
            // content presenter, not the items presenter: the slider drag scales the items presenter.
            _rowSpace.SizeChanged += (_, e) =>
            {
                if (e.WidthChanged)
                    RechunkRowsKeepingPlace();
            };
            _albumScrollViewer.ScrollChanged += AlbumScrollViewer_ScrollChanged;
            ScrollWheelDampingHelper.Attach(_albumScrollViewer);
            emptyLibraryOverlay.AddMusicFolderRequested += (_, __) => AddMusicFolderRequested?.Invoke(this, EventArgs.Empty);
            AlbumGrid.ItemsSource = _rows;
            AlbumGridItem.ArtPx = GridArtPx;

            _collectionChangeRebuildDebounce = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            _collectionChangeRebuildDebounce.Tick += (_, __) =>
            {
                _collectionChangeRebuildDebounce.Stop();
                _itemsSourceCount = TryGetCount(_itemsSource);
                UpdateEmptyLibraryOverlay();
                RefreshAlbumGridFromLibrary();
            };

            Loaded += (_, __) =>
            {
                UpdateSortBarVisibility();
                SyncSectionHeaderWidth();
                UpdateEmptyLibraryOverlay();
            };
            Unloaded += AlbumsView_OnUnloaded;
            SizeChanged += AlbumsView_SizeChanged;
        }

        internal void SyncSectionHeaderWidth()
        {
            double pad = _albumScrollViewer.Padding.Left + _albumScrollViewer.Padding.Right;
            SectionHeaderWidth = Math.Max(100, AvailableRowWidth() - pad);
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
                if (_itemsSourceCollectionNotify != null)
                {
                    _itemsSourceCollectionNotify.CollectionChanged -= OnItemsSourceCollectionChangedForEmptyOverlay;
                    _itemsSourceCollectionNotify = null;
                }

                // Canceling a build mid-way leaves a partial grid; mark it stale now (not in the run's
                // finally, which lands later) so coming straight back rebuilds.
                if (_isRebuilding)
                    _lastGridBuildBrowseMode = null;
                // Art prefetch keeps running: it only fills the cache, which the next visit uses.
                _rebuildCts?.Cancel();
                _rebuildCts?.Dispose();
                _rebuildCts = null;
                _collectionChangeRebuildDebounce.Stop();
            }
            catch
            {
            }
        }
    }
}
