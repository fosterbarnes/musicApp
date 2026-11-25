using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using musicApp;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using musicApp.Constants;
using musicApp.Helpers;

namespace musicApp.Views
{
    public partial class AlbumsView
    {
        private INotifyCollectionChanged? _itemsSourceCollectionNotify;

        public IEnumerable? ItemsSource
        {
            get => _itemsSource;
            set
            {
                int newCount = TryGetCount(value);
                if (ReferenceEquals(_itemsSource, value) && newCount == _itemsSourceCount && _entries.Count > 0 &&
                    BrowseMode == _lastGridBuildBrowseMode)
                {
                    UpdateEmptyLibraryOverlay();
                    return;
                }

                if (_itemsSourceCollectionNotify != null)
                {
                    _itemsSourceCollectionNotify.CollectionChanged -= OnItemsSourceCollectionChangedForEmptyOverlay;
                    _itemsSourceCollectionNotify = null;
                }

                _itemsSource = value;
                _itemsSourceCount = newCount;

                if (value is INotifyCollectionChanged incc)
                {
                    _itemsSourceCollectionNotify = incc;
                    incc.CollectionChanged += OnItemsSourceCollectionChangedForEmptyOverlay;
                }

                UpdateEmptyLibraryOverlay();
                ScheduleGridRebuild();
            }
        }

        // Startup and scans fire several rebuild requests back to back; each one used to
        // cancel and restart the full grouping. Collapse bursts into a single rebuild.
        private DispatcherTimer? _rebuildCoalesceTimer;

        private void ScheduleGridRebuild()
        {
            if (_rebuildCoalesceTimer == null)
            {
                _rebuildCoalesceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                _rebuildCoalesceTimer.Tick += (_, __) =>
                {
                    _rebuildCoalesceTimer!.Stop();
                    _ = RebuildAlbumItemsAsync(preserveViewState: true);
                };
            }
            _rebuildCoalesceTimer.Stop();
            _rebuildCoalesceTimer.Start();
        }

        public event EventHandler? AddMusicFolderRequested;

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
        public event EventHandler<IReadOnlyList<Song>>? RemoveFromLibraryRequested;
        public event EventHandler<Song>? DeleteRequested;
        public event EventHandler<string>? ArtistNavigationRequested;
        public event EventHandler<string>? GenreNavigationRequested;

        public void RebuildColumns() { }

        public bool IsGridCurrentFor(AlbumsBrowseMode mode, IEnumerable? source)
        {
            return !_isRebuilding
                && _entries.Count > 0
                && BrowseMode == mode
                && mode == _lastGridBuildBrowseMode
                && ReferenceEquals(_itemsSource, source);
        }

        public void RefreshAlbumGridFromLibrary() => ScheduleGridRebuild();

        public void SetBrowseModeAndSource(AlbumsBrowseMode mode, IEnumerable? source)
        {
            _suppressBrowseModeRebuild = true;
            try { BrowseMode = mode; }
            finally { _suppressBrowseModeRebuild = false; }
            ItemsSource = source;
        }

        public bool TryRefreshAlbumGroupInPlace(Song track)
        {
            if (track == null || !TryGetAlbumGridGroupKey(track, out var albumTitle, out var artistKey))
                return false;

            var matchedAny = false;

            foreach (var item in _albums)
            {
                if (!string.Equals(item.AlbumTitle, albumTitle, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(item.Artist, artistKey, StringComparison.OrdinalIgnoreCase))
                {
                    // The song left the group this tile was built for: only a rebuild drops or re-picks that tile.
                    if (ReferenceEquals(item.RepresentativeTrack, track))
                        return false;
                    continue;
                }

                matchedAny = true;
                AlbumGridArtCache.Invalidate(item.ArtKey); // the edit may have changed the art
                item.NotifyArtChanged();
            }

            if (!matchedAny)
                return false;

            if (_currentFlyout != null)
            {
                bool flyoutIsGroup =
                    string.Equals(_currentFlyout.AlbumTitle, albumTitle, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(_currentFlyout.Artist, artistKey, StringComparison.OrdinalIgnoreCase);
                if (flyoutIsGroup)
                    PatchOpenFlyoutForGroup(albumTitle, artistKey);
                else if (_currentFlyout.Tracks.Contains(track))
                    return false; // open flyout of the song's old album still lists it
            }

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

            if (_isRebuilding)
            {
                _pendingAlbumSelection = (albumName, artistName, openDetails, selectedTrackFilePath);
                return;
            }

            var item = FindAlbumItem(albumName, artistName);
            if (item == null)
            {
                _pendingAlbumSelection = (albumName, artistName, openDetails, selectedTrackFilePath);
                return;
            }

            _pendingAlbumSelection = null;
            SelectAlbumItem(item, openDetails, bringTargetIntoView: true);

            ApplyProgrammaticFlyoutSelection(string.IsNullOrWhiteSpace(selectedTrackFilePath) ? null : selectedTrackFilePath);
        }

        private void ApplyMergedAlbumSelection(
            (string albumName, string? artistName, bool openDetails, string? selectedTrackFilePath) p,
            bool bringTargetIntoView)
        {
            if (string.IsNullOrWhiteSpace(p.albumName))
                return;

            var item = FindAlbumItem(p.albumName, p.artistName);
            if (item == null)
            {
                _pendingAlbumSelection = p;
                return;
            }

            _pendingAlbumSelection = null;
            SelectAlbumItem(item, p.openDetails, bringTargetIntoView);
            ApplyProgrammaticFlyoutSelection(string.IsNullOrWhiteSpace(p.selectedTrackFilePath) ? null : p.selectedTrackFilePath);
        }

        private AlbumGridItem? FindAlbumItem(string albumName, string? artistName)
        {
            if (!string.IsNullOrWhiteSpace(artistName))
            {
                var match = _albums.FirstOrDefault(a =>
                    string.Equals(a.AlbumTitle, albumName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(a.Artist, artistName, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match;
            }

            return _albums.FirstOrDefault(a =>
                string.Equals(a.AlbumTitle, albumName, StringComparison.OrdinalIgnoreCase));
        }

        private void SelectAlbumItem(AlbumGridItem album, bool openDetails, bool bringTargetIntoView = true)
        {
            if (openDetails)
            {
                if (!ReferenceEquals(_selectedAlbum, album) || _currentFlyout == null)
                    ShowAlbumDetail(album, bringFlyoutIntoView: bringTargetIntoView);
                else if (bringTargetIntoView && _currentFlyout is { } open)
                    Dispatcher.InvokeAsync(() => BringFlyoutFullyIntoView(open), DispatcherPriority.Loaded);

                // Flyout BringIntoView already handles scroll; tile BringIntoView races and
                // leaves the tall flyout below the viewport.
                return;
            }

            if (!bringTargetIntoView)
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                int rowIndex = RowIndexOf(album);
                if (rowIndex >= 0)
                    ItemsHost?.BringIndexIntoViewPublic(rowIndex);
            }), DispatcherPriority.Background);
        }

        private static int TryGetCount(IEnumerable? source)
        {
            if (source is ICollection col)
                return col.Count;
            return -1;
        }

        private void OnItemsSourceCollectionChangedForEmptyOverlay(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // Overlay check is a Count read; doing it now stops the "add music" icon lingering over
            // the first tiles at startup. The rebuild stays debounced.
            UpdateEmptyLibraryOverlay();
            _collectionChangeRebuildDebounce.Stop();
            _collectionChangeRebuildDebounce.Start();
        }

        private void UpdateEmptyLibraryOverlay()
        {
            if (emptyLibraryOverlay == null)
                return;
            emptyLibraryOverlay.Visibility = EmptyLibraryAddOverlay.IsTrackLibraryEmpty(_itemsSource)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void AlbumSizeSlider_DragStarted(object sender, DragStartedEventArgs e)
        {
            CloseAlbumDetail();
            // Read before the drag's LayoutTransform scales the extent; read on release it is off
            // by the scale factor and the view jumped (small -> large landed near the top).
            _dragStartScrollRatio = GetScrollRatio();
            _isDraggingSlider = true;
            _dragStartTileSize = CurrentTileSize;
            _dragTargetSize = CurrentTileSize;
            _dragScaleTransform = new ScaleTransform(1, 1);
            _itemsPresenter.LayoutTransform = _dragScaleTransform;
        }

        private void AlbumSizeSlider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            _isDraggingSlider = false;
            _itemsPresenter.LayoutTransform = Transform.Identity;
            _dragScaleTransform = null;
            CurrentTileSize = _dragTargetSize;
            RestoreScrollRatio(_dragStartScrollRatio);
        }

        private double _dragStartScrollRatio;

        private double GetScrollRatio() =>
            _albumScrollViewer.ExtentHeight > 0
                ? _albumScrollViewer.VerticalOffset / _albumScrollViewer.ExtentHeight
                : 0;

        /// <summary>Lays out the new tile size now and scrolls back to the same share of the list.</summary>
        private void RestoreScrollRatio(double ratio)
        {
            _albumScrollViewer.UpdateLayout();
            _albumScrollViewer.ScrollToVerticalOffset(ratio * _albumScrollViewer.ExtentHeight);
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
            double vw = AvailableRowWidth();
            double vh = _albumScrollViewer.ViewportHeight > 0 ? _albumScrollViewer.ViewportHeight : 400;
            return new AlbumGridLayoutHint(vw, vh, CurrentTileSize, TileMargin.Right, TileMargin.Bottom, TileScaleRatio);
        }

        private static void GetTileStrides(in AlbumGridLayoutHint h, out double tileStrideX, out double tileStrideY)
        {
            AlbumGridLayoutMath.GetStrides(h.TileSize, h.MarginRight, h.MarginBottom, h.TileScaleRatio, out tileStrideX, out tileStrideY);
        }

        private static int ComputePrefixGoal(IReadOnlyList<AlbumGridItem> grouped, int? targetIndex, in AlbumGridLayoutHint hint)
        {
            if (grouped.Count == 0)
                return 0;
            GetTileStrides(hint, out var sx, out var sy);
            double vw = hint.ViewportWidth > 0 ? hint.ViewportWidth : UILayoutConstants.AlbumWrapFallbackWidth;
            double vh = hint.ViewportHeight > 0 ? hint.ViewportHeight : 400;
            int perRow = AlbumGridLayoutMath.PerRowFromViewport(vw, sx);
            int visibleRows = Math.Max(1, (int)Math.Ceiling(Math.Max(1, vh) / sy));
            // The panel realizes the view plus one page below it (default CacheLength: a page each side).
            int realized = (visibleRows * 2 + 1) * perRow;
            return Math.Min(grouped.Count, (targetIndex ?? -1) + 1 + realized);
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

        private static string AlbumGroupArtist(Song t) =>
            !string.IsNullOrWhiteSpace(t.AlbumArtist) ? t.AlbumArtist : t.Artist ?? string.Empty;

        // Grouped ignoring case like album lookups and play order do, so "OK Computer" and "Ok Computer" are one tile.
        private static string AlbumGroupKey(Song t) => (t.Album ?? string.Empty) + "\u001F" + AlbumGroupArtist(t);

        private static List<AlbumGridItem> BuildGroupedAlbums(IEnumerable<Song> songsEnumerable, AlbumSortMode sortMode, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            List<Song> songs;
            try { songs = songsEnumerable.ToList(); }
            catch { return new List<AlbumGridItem>(); }

            var query = songs
                .Where(t => !string.IsNullOrWhiteSpace(t.Album) && t.Album != "Unknown Album")
                .GroupBy(AlbumGroupKey, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var rep = g.First();
                    return new AlbumGridItem(rep.Album ?? string.Empty, AlbumGroupArtist(rep), rep);
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

            // Same groups and order as playing from this view (RecentlyAddedPlaybackOrder).
            return RecentlyAddedPlaybackOrder.BuildGroups(songs)
                .Select(g =>
                {
                    var rep = g.Tracks.MaxBy(t => t.DateAdded)!;
                    var item = g.IsSingleSong
                        ? new AlbumGridItem(SingleSongTileTitle(rep), rep.Artist ?? string.Empty, rep) { IsSingleSong = true }
                        : new AlbumGridItem(rep.Album ?? string.Empty, AlbumGroupArtist(rep), rep);
                    return (item, g.LastAdded);
                })
                .ToList();
        }

        // Untagged files read as "Unknown Title"; the file name says more.
        private static string SingleSongTileTitle(Song s) =>
            string.IsNullOrWhiteSpace(s.Title) || s.Title == "Unknown Title"
                ? System.IO.Path.GetFileNameWithoutExtension(s.FilePath)
                : s.Title;

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

        private (double? scrollRatio, double? scrollOffsetPixels, (string albumName, string? artistName, bool openDetails, string? selectedTrackFilePath)? mergedPending)
            CaptureRebuildRestoreStateAndCloseFlyout(bool preserveViewState)
        {
            double? scrollRatio = null;
            double? scrollOffsetPixels = null;
            (string albumName, string? artistName, bool openDetails, string? selectedTrackFilePath)? mergedPending = null;

            if (preserveViewState)
            {
                if (_albumScrollViewer.ExtentHeight > 0)
                {
                    scrollRatio = _albumScrollViewer.VerticalOffset / _albumScrollViewer.ExtentHeight;
                    scrollOffsetPixels = _albumScrollViewer.VerticalOffset;
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
            _isRebuilding = true;
            // Only the newest run clears _isRebuilding (in the finally below): a superseded run that stopped
            // early must not report idle while the newer one is still building.
            int generation = ++_rebuildGeneration;

            (double? scrollRatio, double? scrollOffsetPixels, var mergedPending) = Dispatcher.CheckAccess()
                ? CaptureRebuildRestoreStateAndCloseFlyout(preserveViewState)
                : await Dispatcher.InvokeAsync(() => CaptureRebuildRestoreStateAndCloseFlyout(preserveViewState)).Task.ConfigureAwait(false);

            AlbumGridArtCache.ForgetMissing(); // a rebuild retries albums that had no art, as before the cache
            _rebuildCts?.Cancel();
            _rebuildCts?.Dispose();
            _rebuildCts = new CancellationTokenSource();
            var ct = _rebuildCts.Token;

            if (_itemsSource is not IEnumerable<Song> songsRef)
            {
                _isRebuilding = false;
                return;
            }

            int songCount = TryGetCount(_itemsSource);
            if (songCount < 0 && songsRef is ICollection colSong)
                songCount = colSong.Count;

            void ReportPhase(AlbumsGridRebuildPhase phase, int done = 0, int total = 0) =>
                AlbumGridRebuildStatus?.Invoke(phase, done, total, songCount);

            bool finished = false;
            try
            {
                ReportPhase(AlbumsGridRebuildPhase.Grouping);

                var sortMode = _sortMode;
                var browseMode = BrowseMode;
                // Albums and Recently Added are different lists: the old scroll offset means nothing in
                // the other one, so a mode switch starts at the top.
                bool modeChanged = _lastGridBuildBrowseMode != browseMode;
                if (modeChanged)
                {
                    scrollRatio = null;
                    scrollOffsetPixels = null;
                }
                // Claim the mode now, not at the end: a switch back to the old mode mid-build must
                // reschedule instead of hitting the ItemsSource fast path.
                _lastGridBuildBrowseMode = browseMode;

                AlbumGridLayoutHint layoutHint = Dispatcher.CheckAccess()
                    ? CaptureAlbumGridLayoutHint()
                    : await Dispatcher.InvokeAsync(CaptureAlbumGridLayoutHint).Task.ConfigureAwait(false);

                // Copy the live library here (UI thread): enumerating it on the worker while a scan adds
                // tracks threw "collection was modified", and the catch turned that into an empty grid.
                var songsSnapshot = songsRef.ToList();

                (List<object> entries, List<AlbumGridItem> albums) = await Task.Run(() =>
                {
                    if (browseMode == AlbumsBrowseMode.RecentlyAdded)
                    {
                        var pairs = BuildGroupedAlbumsByRecentlyAdded(songsSnapshot, ct);
                        var r = BuildRecentlyAddedRows(pairs, DateTime.Now);
                        return (r, r.OfType<AlbumGridItem>().ToList());
                    }
                    var grouped = BuildGroupedAlbums(songsSnapshot, sortMode, ct);
                    return (grouped.Cast<object>().ToList(), grouped);
                }, ct).ConfigureAwait(false);

                // Decode the first screen off the UI thread so the first frame binds to ready art.
                int? targetIndex = mergedPending is { } p ? FindTargetAlbumIndex(albums, p.albumName, p.artistName) : null;
                int prefixGoal = ComputePrefixGoal(albums, targetIndex, layoutHint);
                ReportPhase(AlbumsGridRebuildPhase.LoadingArtwork, 0, albums.Count);
                await Task.Run(() => PrefetchArtNow(albums, 0, prefixGoal, AlbumGridItem.ArtPx, ct), ct).ConfigureAwait(false);

                await Dispatcher.InvokeAsync(() =>
                {
                    if (ct.IsCancellationRequested)
                        return; // a newer run owns the grid now
                    _entries = entries;
                    _albums = albums;
                    RebuildRows();
                    if (modeChanged)
                        _albumScrollViewer.ScrollToTop();
                    AlbumGrid.UpdateLayout();

                    bool restoredExternalSelection = false;
                    if (_pendingAlbumSelection.HasValue)
                    {
                        var ps = _pendingAlbumSelection.Value;
                        _pendingAlbumSelection = null;
                        ApplyMergedAlbumSelection(ps, bringTargetIntoView: true);
                        restoredExternalSelection = true;
                    }
                    else if (mergedPending.HasValue)
                    {
                        ApplyMergedAlbumSelection(mergedPending.Value, bringTargetIntoView: false);
                    }

                    if (!restoredExternalSelection && preserveViewState)
                    {
                        AlbumGrid.UpdateLayout();
                        double y = scrollOffsetPixels
                            ?? (scrollRatio.HasValue ? scrollRatio.Value * _albumScrollViewer.ExtentHeight : double.NaN);
                        if (!double.IsNaN(y))
                            _albumScrollViewer.ScrollToVerticalOffset(Math.Min(Math.Max(0, y), _albumScrollViewer.ScrollableHeight));
                    }

                    finished = true;
                    _isRebuilding = false;
                    StartArtPrefetch(FirstVisibleAlbumIndex()); // reports progress, then Complete
                }, DispatcherPriority.Loaded).Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                // A superseded run leaves state and status to the newer one.
                if (generation == _rebuildGeneration && !finished)
                {
                    _isRebuilding = false;
                    _lastGridBuildBrowseMode = null; // canceled mid-build: the grid is partial
                    ReportPhase(AlbumsGridRebuildPhase.Complete);
                }
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
