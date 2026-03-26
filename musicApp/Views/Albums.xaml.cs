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
        private CancellationTokenSource? _artLoadCts;
        private Song? _contextMenuSong;
        
        private (string albumName, string? artistName, bool openDetails, string? selectedTrackFilePath)? _pendingAlbumSelection;
        private readonly DispatcherTimer _artLoadDebounce;
        private readonly DispatcherTimer _flyoutResizeDebounce;

        private AlbumGridItem? _selectedAlbum;
        private AlbumFlyoutItem? _currentFlyout;
        private bool _isRefreshingFlyoutLayout;

        private bool _isDraggingSlider;
        private double _dragStartTileSize;
        private double _dragTargetSize;
        private ScaleTransform? _dragScaleTransform;

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

            Loaded += async (_, __) => await LoadVisibleAlbumArtAsync();
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
                _artLoadCts?.Cancel();
                _artLoadCts?.Dispose();
                _artLoadCts = null;
                _artLoadDebounce.Stop();
                _flyoutResizeDebounce.Stop();
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

        public IEnumerable? ItemsSource
        {
            get => _itemsSource;
            set
            {
                int newCount = TryGetCount(value);
                if (ReferenceEquals(_itemsSource, value) && newCount == _itemsSourceCount)
                    return;

                _itemsSource = value;
                _itemsSourceCount = newCount;
                _ = RebuildAlbumItemsAsync();
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

        public void RefreshAlbumGridFromLibrary() => _ = RebuildAlbumItemsAsync();

        public void ScrollToAlbum(string albumName)
        {
            SelectAlbum(albumName, null, openDetails: true, selectedTrackFilePath: null);
        }

        /// <summary>
        /// Selects the matching album tile for a track and opens its flyout.
        /// </summary>
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
            SelectAlbumItem(item, openDetails);

            SelectedFlyoutTrackFilePath = selectedTrackFilePath;
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

        private void SelectAlbumItem(AlbumGridItem album, bool openDetails)
        {
            if (openDetails)
            {
                if (!ReferenceEquals(_selectedAlbum, album) || _currentFlyout == null)
                    ShowAlbumDetail(album);
                else
                    RefreshOpenFlyoutLayout();
            }

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

        private async Task RebuildAlbumItemsAsync()
        {
            CloseAlbumDetail();
            _rebuildCts?.Cancel();
            _rebuildCts?.Dispose();
            _rebuildCts = new CancellationTokenSource();
            var ct = _rebuildCts.Token;

            _albumItems.Clear();

            if (_itemsSource is not IEnumerable<Song> songsEnumerable)
                return;

            List<Song> songs;
            try { songs = songsEnumerable.ToList(); }
            catch { return; }

            List<AlbumGridItem> grouped;
            try
            {
                grouped = await Task.Run(() =>
                {
                    var query = songs
                        .Where(t => !string.IsNullOrWhiteSpace(t.Album) && t.Album != "Unknown Album")
                        .GroupBy(t =>
                        {
                            var albumArtist = !string.IsNullOrWhiteSpace(t.AlbumArtist)
                                ? t.AlbumArtist
                                : t.Artist ?? string.Empty;
                            return (Album: t.Album ?? string.Empty, Artist: albumArtist);
                        })
                        .Select(g => new AlbumGridItem(g.Key.Album, g.Key.Artist, g.First()));

                    query = _sortMode switch
                    {
                        AlbumSortMode.Artist => query
                            .OrderBy(a => a.Artist, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(a => a.AlbumTitle, StringComparer.OrdinalIgnoreCase),
                        _ => query
                            .OrderBy(a => a.AlbumTitle, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(a => a.Artist, StringComparer.OrdinalIgnoreCase)
                    };

                    return query.ToList();
                }, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }

            if (ct.IsCancellationRequested) return;

            const int batchSize = UILayoutConstants.AlbumRebuildBatchSize;
            for (int i = 0; i < grouped.Count; i += batchSize)
            {
                if (ct.IsCancellationRequested) break;

                int end = Math.Min(grouped.Count, i + batchSize);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (i == 0) _albumItems.Clear();
                    for (int j = i; j < end; j++)
                        _albumItems.Add(grouped[j]);
                });

                await Task.Yield();
            }

            if (!ct.IsCancellationRequested)
            {
                _ = LoadInitialAlbumArtAsync();

                if (_pendingAlbumSelection.HasValue)
                {
                    var pending = _pendingAlbumSelection.Value;
                    await Dispatcher.InvokeAsync(() =>
                        SelectAlbum(pending.albumName, pending.artistName, pending.openDetails, pending.selectedTrackFilePath));
                }
            }
        }

        // --- Art loading ---

        private async Task LoadInitialAlbumArtAsync()
        {
            if (_albumItems.Count == 0) return;

            _artLoadCts?.Cancel();
            _artLoadCts?.Dispose();
            _artLoadCts = new CancellationTokenSource();
            var ct = _artLoadCts.Token;

            int maxInitial = Math.Min(UILayoutConstants.InitialAlbumArtLoadCount, _albumItems.Count);
            var itemsToLoad = Enumerable.Range(0, maxInitial)
                .Select(i => _albumItems[i] as AlbumGridItem)
                .Where(a => a != null && a.AlbumArtSource == null)
                .Cast<AlbumGridItem>()
                .ToList();

            try { await LoadAlbumArtForItemsAsync(itemsToLoad, ct); }
            catch (OperationCanceledException) { }
        }

        private async Task LoadVisibleAlbumArtAsync()
        {
            if (AlbumScrollViewer == null || _albumItems.Count == 0) return;

            _artLoadCts?.Cancel();
            _artLoadCts?.Dispose();
            _artLoadCts = new CancellationTokenSource();
            var ct = _artLoadCts.Token;

            var visible = GetVisibleIndices();
            if (visible.Count == 0) return;

            int firstIdx = Math.Max(0, visible[0] - UILayoutConstants.AlbumVisibleRangeOverscan);
            int lastIdx = Math.Min(_albumItems.Count - 1, visible[^1] + UILayoutConstants.AlbumVisibleRangeOverscan);

            var itemsToLoad = new List<AlbumGridItem>();
            for (int i = firstIdx; i <= lastIdx; i++)
            {
                if (_albumItems[i] is AlbumGridItem a && a.AlbumArtSource == null)
                    itemsToLoad.Add(a);
            }

            try { await LoadAlbumArtForItemsAsync(itemsToLoad, ct); }
            catch (OperationCanceledException) { }
        }

        private async Task LoadAlbumArtForItemsAsync(IReadOnlyCollection<AlbumGridItem> items, CancellationToken ct)
        {
            if (items.Count == 0)
                return;

            int targetSize = Math.Max(UILayoutConstants.AlbumArtMinimumTargetSize, (int)Math.Round(CurrentTileSize));
            using var throttler = new SemaphoreSlim(4);
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
                    });
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
            _ = RebuildAlbumItemsAsync();
        }

        // --- Album detail flyout ---

        private void ShowAlbumDetail(AlbumGridItem album)
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

            if (album.AlbumArtSource == null)
            {
                _ = Task.Run(() =>
                {
                    var img = AlbumArtThumbnailHelper.LoadForTrack(album.RepresentativeTrack);
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
            _ = Task.Run(() =>
            {
                var full = AlbumArtThumbnailHelper.LoadFullSizeForTrack(album.RepresentativeTrack);
                if (full != null)
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        if (_selectedAlbum == album && _currentFlyout == flyout)
                            flyout.AlbumArtSource = full;
                    });
                }
            });

            if (_itemsSource is IEnumerable<Song> songs)
            {
                var albumArtist = album.Artist;
                flyout.Tracks = songs
                    .Where(s =>
                        string.Equals(s.Album, album.AlbumTitle, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(
                            !string.IsNullOrWhiteSpace(s.AlbumArtist) ? s.AlbumArtist : s.Artist,
                            albumArtist,
                            StringComparison.OrdinalIgnoreCase))
                    .OrderBy(s => int.TryParse(s.DiscNumber, out int d) ? d : 0)
                    .ThenBy(s => s.TrackNumber)
                    .ToList();

                int half = (int)Math.Ceiling(flyout.Tracks.Count / 2.0);
                flyout.TracksColumn1 = flyout.Tracks.Take(half).ToList();
                flyout.TracksColumn2 = flyout.Tracks.Skip(half).ToList();
                flyout.AlbumMetadata = BuildAlbumMetadata(flyout.Tracks);
            }

            FlyoutPanelHeight = CalculateFlyoutHeight(flyout.Tracks.Count);

            int albumIndex = _albumItems.IndexOf(album);
            int insertIndex = GetRowEndIndex(albumIndex);

            FlyoutPanelWidth = GetWrapPanelWidth();
            UpdateFlyoutArrowOffset(album);

            _currentFlyout = flyout;
            _albumItems.Insert(insertIndex, flyout);

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
            ScheduleArtLoad();
            if (_currentFlyout == null || _selectedAlbum == null)
                return;

            // Keep width in sync while the window is actively resizing.
            FlyoutPanelWidth = GetWrapPanelWidth();
            _flyoutResizeDebounce.Stop();
            _flyoutResizeDebounce.Start();
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
        private void AlbumContextMenu_AddToPlaylistClick(object sender, RoutedEventArgs e) { }
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
