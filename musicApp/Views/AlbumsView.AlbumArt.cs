using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using musicApp.Constants;
using musicApp.Helpers;

namespace musicApp.Views
{
    public partial class AlbumsView
    {
        private void ScheduleArtLoad()
        {
            _artLoadDebounce.Stop();
            _artLoadDebounce.Start();
        }

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

            AlbumGridLayoutMath.GetStrides(
                CurrentTileSize, TileMargin.Right, TileMargin.Bottom, TileScaleRatio,
                out double tileStrideX, out double tileStrideY);
            int perRow = AlbumGridLayoutMath.PerRowFromViewport(vw, tileStrideX);
            int rowCount = Math.Max(
                UILayoutConstants.AlbumArtViewportBootstrapMinRows,
                (int)Math.Ceiling(vh / tileStrideY) + UILayoutConstants.AlbumVisibleRangeOverscan);
            if (BrowseMode == AlbumsBrowseMode.RecentlyAdded)
                rowCount += 6;

            lastIdx = Math.Min(_albumItems.Count - 1, perRow * rowCount - 1);
        }

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
            SyncSectionHeaderWidth();
            if (e.VerticalChange == 0 && e.ExtentHeightChange == 0)
                return;

            ScheduleArtLoad();
        }
    }
}
