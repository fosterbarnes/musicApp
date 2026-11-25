using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;
using musicApp.Constants;
using musicApp.Helpers;

namespace musicApp.Views
{
    // Grid art: tiles read AlbumGridArtCache synchronously when they bind (AlbumGridItem.AlbumArtSource), so
    // art is never seen loading. This file keeps the cache filled around the view so those reads are hits; the
    // cache is byte-bounded, so the prefetch window follows the scroll position.
    public partial class AlbumsView
    {
        private CancellationTokenSource? _artPrefetchCts;
        private bool _artPrefetchReporting; // a running prefetch owns the status bar's "loading artwork"
        private DispatcherTimer? _scrollPrefetchDebounce;

        /// <summary>
        /// Decode size for grid art: the tile size in DIPs, as before the cache. Measured 2026-11-25: decoding at
        /// display pixels (x1.5) cost 2.25x the memory and overflowed the cache budget (evict/re-decode churn).
        /// </summary>
        private int GridArtPx => Math.Max(UILayoutConstants.AlbumArtMinimumTargetSize, (int)Math.Round(CurrentTileSize));

        private static int ArtDecodeParallelism => Math.Clamp(Environment.ProcessorCount, 4, 12);

        /// <summary>
        /// Decodes the albums around <paramref name="startIndex"/> (as many as fit the cache budget, two ahead for
        /// each one behind) on worker threads. Replaces any earlier prefetch and outlives the view being unloaded.
        /// <paramref name="reportStatus"/> shows progress in the status bar; a replaced reporting prefetch hands
        /// that over, so "loading artwork" always ends.
        /// </summary>
        private void StartArtPrefetch(int startIndex, bool reportStatus = true)
        {
            _artPrefetchCts?.Cancel();
            _artPrefetchCts?.Dispose();
            _artPrefetchCts = new CancellationTokenSource();
            var ct = _artPrefetchCts.Token;
            var albums = _albums;
            int px = AlbumGridItem.ArtPx;
            int total = Math.Min(albums.Count, AlbumGridArtCache.AlbumsInBudget(px));
            int songCount = TryGetCount(_itemsSource);
            bool report = reportStatus || _artPrefetchReporting;
            _artPrefetchReporting = report;
            void Report(AlbumsGridRebuildPhase phase, int done)
            {
                if (report)
                    AlbumGridRebuildStatus?.Invoke(phase, done, total, songCount);
            }

            _ = Task.Run(() =>
            {
                int done = 0;
                try
                {
                    var options = new ParallelOptions { MaxDegreeOfParallelism = ArtDecodeParallelism, CancellationToken = ct };
                    Parallel.ForEach(NearestFirst(albums.Count, startIndex, total), options, i =>
                    {
                        AlbumGridArtCache.GetOrLoad(albums[i].ArtKey, albums[i].RepresentativeTrack, px);
                        int d = Interlocked.Increment(ref done);
                        if (d % 24 == 0 && !ct.IsCancellationRequested)
                            Report(AlbumsGridRebuildPhase.LoadingArtwork, d);
                    });
                }
                catch (OperationCanceledException)
                {
                    return; // whoever canceled started the newer prefetch, which reports
                }
                Report(AlbumsGridRebuildPhase.Complete, total);
                Dispatcher.BeginInvoke(() =>
                {
                    if (!ct.IsCancellationRequested)
                        _artPrefetchReporting = false;
                });
            }, ct);
        }

        /// <summary>Scrolling moves the prefetch window with the view (once the scroll settles briefly).</summary>
        private void AlbumScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange == 0 || _albums.Count == 0)
                return;
            if (_scrollPrefetchDebounce == null)
            {
                _scrollPrefetchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                _scrollPrefetchDebounce.Tick += (_, _) =>
                {
                    _scrollPrefetchDebounce.Stop();
                    StartArtPrefetch(FirstVisibleAlbumIndex(), reportStatus: false);
                };
            }
            _scrollPrefetchDebounce.Stop();
            _scrollPrefetchDebounce.Start();
        }

        /// <summary>Decodes <paramref name="count"/> albums from <paramref name="from"/> in parallel, blocking until done.</summary>
        private static void PrefetchArtNow(IReadOnlyList<AlbumGridItem> albums, int from, int count, int px, CancellationToken ct = default)
        {
            from = Math.Clamp(from, 0, albums.Count);
            int to = Math.Min(albums.Count, from + Math.Max(0, count));
            var options = new ParallelOptions { MaxDegreeOfParallelism = ArtDecodeParallelism, CancellationToken = ct };
            Parallel.For(from, to, options, i => AlbumGridArtCache.GetOrLoad(albums[i].ArtKey, albums[i].RepresentativeTrack, px));
        }

        /// <summary>Up to <paramref name="limit"/> indices outward from <paramref name="start"/>, two ahead per one behind.</summary>
        private static IEnumerable<int> NearestFirst(int count, int start, int limit)
        {
            int ahead = Math.Clamp(start, 0, count), behind = ahead - 1, n = 0;
            while (n < limit && (ahead < count || behind >= 0))
            {
                for (int k = 0; k < 2 && ahead < count && n < limit; k++, n++)
                    yield return ahead++;
                if (behind >= 0 && n < limit)
                {
                    yield return behind--;
                    n++;
                }
            }
        }

        /// <summary>
        /// Tile size changed: decode the albums around the view at the new size first (parallel, ~tens of ms on a
        /// cold cache), then re-chunk the rows so the new tiles bind to ready art, then refill the rest.
        /// </summary>
        private void OnTileSizeChanged()
        {
            AlbumGridItem.ArtPx = GridArtPx;
            if (_entries.Count == 0 || _isDraggingSlider)
                return;

            int first = FirstVisibleAlbumIndex();
            int screen = AlbumsPerScreen();
            // The panel realizes about one screen above and one below the view (default CacheLength).
            PrefetchArtNow(_albums, first - screen, screen * 3, AlbumGridItem.ArtPx);
            RebuildRows();
            StartArtPrefetch(first);
        }
    }
}
