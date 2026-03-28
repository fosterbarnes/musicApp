using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using musicApp;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using musicApp.Constants;
using musicApp.Helpers;

namespace musicApp.Views
{
    public partial class AlbumsView
    {
        public IEnumerable? ItemsSource
        {
            get => _itemsSource;
            set
            {
                int newCount = TryGetCount(value);
                if (ReferenceEquals(_itemsSource, value) && newCount == _itemsSourceCount && _albumItems.Count > 0 &&
                    BrowseMode == _lastGridBuildBrowseMode)
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
            AlbumGridLayoutMath.GetStrides(h.TileSize, h.MarginRight, h.MarginBottom, h.TileScaleRatio, out tileStrideX, out tileStrideY);
        }

        private static int ComputePrefixGoal(IReadOnlyList<AlbumGridItem> grouped, int? targetIndex, in AlbumGridLayoutHint hint)
        {
            if (grouped.Count == 0)
                return 0;
            GetWrapPanelStride(hint, out var sx, out var sy);
            double vw = hint.ViewportWidth > 0 ? hint.ViewportWidth : UILayoutConstants.AlbumWrapFallbackWidth;
            double vh = hint.ViewportHeight > 0 ? hint.ViewportHeight : 400;
            int perRow = AlbumGridLayoutMath.PerRowFromViewport(vw, sx);
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

        private static List<(AlbumGridItem Item, DateTime MaxAdded)> BuildGroupedAlbumsByRecentlyAdded(
            IEnumerable<Song> songsEnumerable,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            List<Song> songs;
            try { songs = songsEnumerable.ToList(); }
            catch { return new List<(AlbumGridItem, DateTime)>(); }

            var list = songs
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
                    var maxAdded = g.Max(t => t.DateAdded).Date;
                    var rep = g.OrderByDescending(t => t.DateAdded).First();
                    BitmapImage? art = null;
                    if (!string.IsNullOrEmpty(rep.ThumbnailCachePath))
                        art = AlbumArtCacheManager.LoadFromCachePath(rep.ThumbnailCachePath);
                    return (new AlbumGridItem(g.Key.Album, g.Key.Artist, rep, art), maxAdded);
                })
                .OrderByDescending(x => x.Item2)
                .ThenBy(x => x.Item1.AlbumTitle, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Item1.Artist, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return list;
        }

        private static List<object> BuildRecentlyAddedRows(
            IReadOnlyList<(AlbumGridItem Item, DateTime MaxAdded)> ordered,
            DateTime nowLocal)
        {
            var rows = new List<object>();
            string? lastTitle = null;
            var culture = CultureInfo.CurrentCulture;
            foreach (var pair in ordered)
            {
                var title = RecentlyAddedTimeline.GetSectionTitle(pair.MaxAdded, nowLocal, culture);
                if (!string.Equals(title, lastTitle, StringComparison.Ordinal))
                {
                    rows.Add(new AlbumSectionHeaderItem(title));
                    lastTitle = title;
                }

                rows.Add(pair.Item1);
            }

            return rows;
        }

        private static int MapAlbumCountToRowExclusiveEnd(IReadOnlyList<object> rows, int albumPrefixGoal)
        {
            if (albumPrefixGoal <= 0)
                return 0;
            int albums = 0;
            for (int r = 0; r < rows.Count; r++)
            {
                if (rows[r] is AlbumGridItem)
                {
                    albums++;
                    if (albums >= albumPrefixGoal)
                        return r + 1;
                }
            }

            return rows.Count;
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
            var browseMode = BrowseMode;

            AlbumGridLayoutHint layoutHint;
            if (Dispatcher.CheckAccess())
                layoutHint = CaptureAlbumGridLayoutHint();
            else
                layoutHint = await Dispatcher.InvokeAsync(CaptureAlbumGridLayoutHint).Task.ConfigureAwait(false);

            List<object> rows;
            List<AlbumGridItem> albumsOnly;
            try
            {
                if (browseMode == AlbumsBrowseMode.RecentlyAdded)
                {
                    (rows, albumsOnly) = await Task.Run(() =>
                    {
                        var pairs = BuildGroupedAlbumsByRecentlyAdded(songsRef, ct);
                        var r = BuildRecentlyAddedRows(pairs, DateTime.Now);
                        return (r, r.OfType<AlbumGridItem>().ToList());
                    }, ct).ConfigureAwait(false);
                }
                else
                {
                    var grouped = await Task.Run(() => BuildGroupedAlbums(songsRef, sortMode, ct), ct).ConfigureAwait(false);
                    albumsOnly = grouped;
                    rows = grouped.Cast<object>().ToList();
                }
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

            if (rows.Count == 0)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    _albumItems.Clear();
                    _lastGridBuildBrowseMode = browseMode;
                    HideLoadingIndicator();
                }, DispatcherPriority.Loaded).Task.ConfigureAwait(false);
                return;
            }

            int? targetIndex = null;
            if (mergedPending.HasValue)
            {
                var p = mergedPending.Value;
                targetIndex = FindTargetAlbumIndex(albumsOnly, p.albumName, p.artistName);
            }

            int prefixGoal = ComputePrefixGoal(albumsOnly, targetIndex, layoutHint);
            int prefixRowEnd = MapAlbumCountToRowExclusiveEnd(rows, prefixGoal);
            int totalRows = rows.Count;

            int i = 0;
            int batchLoopIndex = 0;
            int batchesSinceSample = int.MaxValue;
            SystemResourceSnapshot? lastResourceSnapshot = null;
            int scanConcurrencySmoothed = 0;

            while (i < totalRows && !ct.IsCancellationRequested)
            {
                bool inPrefixPhase = i < prefixRowEnd;
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
                    end = Math.Min(end, prefixRowEnd);
                end = Math.Min(end, totalRows);

                if (end <= i)
                    end = Math.Min(i + UILayoutConstants.AlbumRebuildBatchMin, totalRows);

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
                            _albumItems.Add(rows[j]);

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
                    SyncSectionHeaderWidth();

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

                    _lastGridBuildBrowseMode = browseMode;
                    KickViewportAlbumArtNow();
                    HideLoadingIndicator();
                }, DispatcherPriority.Loaded).Task.ConfigureAwait(false);
            }
        }

        private void SortModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (BrowseMode != AlbumsBrowseMode.AllAlbums)
                return;
            _sortMode = SortModeCombo.SelectedIndex == 1 ? AlbumSortMode.Artist : AlbumSortMode.Album;
            _ = RebuildAlbumItemsAsync(preserveViewState: false);
        }
    }
}
