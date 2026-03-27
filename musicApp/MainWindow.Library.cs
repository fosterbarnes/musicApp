using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using musicApp.Dialogs;
using musicApp.Helpers;

namespace musicApp
{
    public partial class MainWindow
    {
        private const int FruitAppStorefrontBatchDelayMs = 400;

        private static bool TryStorefrontAlbumKey(Song track, out string groupKey)
        {
            var albumNorm = (track.Album ?? "").Trim();
            if (string.IsNullOrWhiteSpace(albumNorm) ||
                string.Equals(albumNorm, "Unknown Album", StringComparison.OrdinalIgnoreCase))
            {
                groupKey = "";
                return false;
            }

            var aa = (track.AlbumArtist ?? "").Trim();
            var ar = (track.Artist ?? "").Trim();
            var artistSearch = !string.IsNullOrWhiteSpace(aa) ? aa : ar;
            groupKey = albumNorm.ToLowerInvariant() + "\0" + artistSearch.ToLowerInvariant();
            return true;
        }

        private async Task AddMusicFolderAsync()
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select a folder containing music files"
            };

            var ok = dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK;
            WindowFocusHelper.ScheduleActivate(this);
            if (ok)
                await LoadMusicFromFolderAsync(dialog.SelectedPath, true, true);
        }

        private async Task RescanLibraryAsync()
        {
            try
            {
                var musicFolders = await libraryManager.GetMusicFoldersAsync();
                if (musicFolders == null || musicFolders.Count == 0)
                {
                    MessageDialog.Show(this, "No Folders", "No music folders have been added yet.", MessageDialog.Buttons.Ok);
                    return;
                }

                await Task.Run(() => AlbumArtCacheManager.InvalidateAll());

                var totalNewTracks = 0;
                foreach (var folderPath in musicFolders)
                {
                    if (Directory.Exists(folderPath))
                    {
                        await LoadMusicFromFolderAsync(folderPath, false);
                        totalNewTracks += allTracks.Count(t => LibraryPathHelper.IsFileUnderMusicFolder(t.FilePath, folderPath));
                    }
                }

                UpdateUI();
                await MaybeRunPostScanSystemArtworkCacheAsync();
                MessageDialog.Show(this, "Library Updated", $"Library re-scanned. Found {totalNewTracks} total tracks across all folders.", MessageDialog.Buttons.Ok);
            }
            catch (Exception ex)
            {
                MessageDialog.Show(this, "Error", $"Error re-scanning library: {ex.Message}", MessageDialog.Buttons.Ok);
            }
        }

        private async Task MaybeRunPostScanSystemArtworkCacheAsync()
        {
            var listCount = allTracks.Count;
            IProgress<(int done, int total)>? progress = null;
            if (listCount > 0)
            {
                progress = new Progress<(int done, int total)>(p =>
                {
                    Dispatcher.BeginInvoke(
                        () => UpdateStatusBarPostScanAlbumWork(p.done, p.total),
                        DispatcherPriority.Normal);
                });
                await Dispatcher.InvokeAsync(() =>
                {
                    progressBarFill.Visibility = Visibility.Visible;
                    progressBarFill.Width = 0;
                    UpdateStatusBarPostScanAlbumWork(0, listCount);
                });
            }

            try
            {
                await ApplyFruitAppStorefrontArtToLibraryAsync(progress, CancellationToken.None).ConfigureAwait(true);
            }
            finally
            {
                if (listCount > 0)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        progressBarFill.Visibility = Visibility.Collapsed;
                        progressBarFill.Width = 0;
                        UpdateStatusBar();
                    });
                }
            }
        }

        private async Task RemoveMusicFolderAsync()
        {
            try
            {
                var musicFolders = await libraryManager.GetMusicFoldersAsync();
                if (musicFolders == null || musicFolders.Count == 0)
                {
                    MessageDialog.Show(this, "No Folders", "No music folders have been added yet.", MessageDialog.Buttons.Ok);
                    return;
                }

                var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Select a folder to remove from the library",
                    ShowNewFolderButton = false
                };

                var dr = dialog.ShowDialog();
                WindowFocusHelper.ScheduleActivate(this);
                if (dr == System.Windows.Forms.DialogResult.OK)
                {
                    var folderToRemove = dialog.SelectedPath;
                    if (musicFolders.Any(f => LibraryPathHelper.PathsEqual(f, folderToRemove)))
                    {
                        var tracksToRemove = allTracks.Where(t => LibraryPathHelper.IsFileUnderMusicFolder(t.FilePath, folderToRemove)).ToList();
                        foreach (var track in tracksToRemove)
                        {
                            RemoveTrackFromCollections(track, includeShuffled: false);
                        }

                        foreach (var playlist in playlists)
                        {
                            var playlistTracksToRemove = playlist.Tracks.Where(t => LibraryPathHelper.IsFileUnderMusicFolder(t.FilePath, folderToRemove)).ToList();
                            foreach (var track in playlistTracksToRemove)
                            {
                                playlist.RemoveTrack(track);
                            }
                        }

                        await libraryManager.RemoveMusicFolderAsync(folderToRemove);
                        await libraryManager.RemoveFolderFromCacheAsync(folderToRemove);

                        UpdateUI();
                        UpdateShuffledTracks();
                        RefreshVisibleViews();
                        UpdateStatusBar();

                        MessageDialog.Show(this, "Folder Removed", $"Folder '{folderToRemove}' and {tracksToRemove.Count} tracks removed from library.", MessageDialog.Buttons.Ok);
                    }
                    else
                    {
                        MessageDialog.Show(this, "Folder Not Found", $"Folder '{folderToRemove}' not found in library.", MessageDialog.Buttons.Ok);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageDialog.Show(this, "Error", $"Error removing folder: {ex.Message}", MessageDialog.Buttons.Ok);
            }
        }

        public Task RunAddMusicFromSettingsAsync() => AddMusicFolderAsync();

        public Task RunRescanLibraryFromSettingsAsync() => RescanLibraryAsync();

        public Task RunRemoveMusicFromSettingsAsync() => RemoveMusicFolderAsync();

        public Task<(int Ok, int Skipped, int Failed)> RunScanMissingRemoteAlbumArtAsync(
            IProgress<RemoteAlbumArtScanUiProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            ScanMissingRemoteAlbumArtToLibraryAsync(progress, cancellationToken);

        private static string FormatAlbumArtScanAlbumLine(Song rep)
        {
            var artist = string.IsNullOrWhiteSpace(rep.Artist) ? "Unknown artist" : rep.Artist.Trim();
            var album = (rep.Album ?? "").Trim();
            if (string.IsNullOrEmpty(album))
                album = "Unknown album";
            var s = $"{artist} – {album}";
            return s.Length <= 72 ? s : s[..69] + "…";
        }

        private async Task<(int Ok, int Skipped, int Failed)> ScanMissingRemoteAlbumArtToLibraryAsync(
            IProgress<RemoteAlbumArtScanUiProgress>? progress,
            CancellationToken cancellationToken)
        {
            var list = allTracks.ToList();
            const int batchSize = 320;
            const int progressEvery = 22;

            if (list.Count == 0)
            {
                progress?.Report(new RemoteAlbumArtScanUiProgress(0, -1, "No tracks in library."));
                progress?.Report(new RemoteAlbumArtScanUiProgress(0, -1, "Updating library index…"));
                await UpdateLibraryCacheAsync();
                await Dispatcher.InvokeAsync(() => RefreshAfterBatchMetadataEdit());
                return (0, 0, 0);
            }

            progress?.Report(new RemoteAlbumArtScanUiProgress(0, list.Count,
                $"Checking tags and embed eligibility… 0/{list.Count}"));

            var candidates = new ConcurrentBag<Song>();
            var classificationFailed = 0;

            int scanConcurrencySmoothed = 0;
            SystemResourceSnapshot? lastResourceSnapshot = null;
            var batchesSinceSample = int.MaxValue;
            var batchIndex = 0;
            var classifiedSoFar = 0;

            foreach (var classifyBatch in list.Chunk(batchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mustSample = batchIndex == 0 || batchesSinceSample >= 2;
                int dop;
                if (mustSample)
                {
                    var sampleInterval = lastResourceSnapshot.HasValue && lastResourceSnapshot.Value.CpuBusyPercent < 40
                        ? TimeSpan.FromMilliseconds(50)
                        : TimeSpan.FromMilliseconds(100);
                    lastResourceSnapshot = await Task.Run(
                            () => WindowsSystemMetrics.Sample(sampleInterval),
                            cancellationToken)
                        .ConfigureAwait(true);
                    dop = ScanConcurrencyAdvisor.Recommend(
                        lastResourceSnapshot.Value,
                        Environment.ProcessorCount,
                        ref scanConcurrencySmoothed);
                    batchesSinceSample = 0;
                }
                else
                    dop = scanConcurrencySmoothed;

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = dop,
                    CancellationToken = cancellationToken
                };

                await Parallel.ForEachAsync(classifyBatch, parallelOptions, async (track, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    var path = track.FilePath;
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                        return;

                    if (!TryStorefrontAlbumKey(track, out _))
                        return;

                    try
                    {
                        var skipForEmbed = await Task.Run(
                                () => EmbeddedCoverEligibility.ShouldSkipSystemCacheEmbed(path),
                                ct)
                            .ConfigureAwait(false);
                        if (skipForEmbed)
                            return;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        Interlocked.Increment(ref classificationFailed);
                        return;
                    }

                    candidates.Add(track);
                });

                classifiedSoFar += classifyBatch.Length;
                progress?.Report(new RemoteAlbumArtScanUiProgress(classifiedSoFar, list.Count,
                    $"Checking tags and embed eligibility… {classifiedSoFar}/{list.Count}"));

                if (!mustSample)
                    batchesSinceSample++;
                batchIndex++;
            }

            var candidateList = candidates.ToList();
            var skipped = list.Count - candidateList.Count;
            var failed = classificationFailed;

            var groups = candidateList
                .GroupBy(
                    t =>
                    {
                        TryStorefrontAlbumKey(t, out var k);
                        return k;
                    },
                    StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => g.ToList())
                .ToList();

            var embedJobs = new List<(Song Track, byte[] Pic, string? Mbid)>();
            using var http = AlbumArtRemoteFetcher.CreateFruitAppSearchHttpClient();
            var networkDelay = false;
            var groupOrdinal = 0;

            progress?.Report(new RemoteAlbumArtScanUiProgress(0, -1,
                $"Found {candidateList.Count} file(s) that may need artwork across {groups.Count} album(s). Attempting to download covers…"));

            foreach (var members in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (members.Count == 0)
                    continue;

                var rep = members[0];
                groupOrdinal++;
                progress?.Report(new RemoteAlbumArtScanUiProgress(groupOrdinal, groups.Count,
                    $"Attempting to download artwork: {FormatAlbumArtScanAlbumLine(rep)} ({groupOrdinal} of {groups.Count})"));
                if (networkDelay)
                    await Task.Delay(FruitAppStorefrontBatchDelayMs, cancellationToken).ConfigureAwait(true);
                networkDelay = true;

                AlbumArtFetchBytesResult fetch;
                try
                {
                    fetch = await AlbumArtRemoteFetcher.FetchCoverBytesAsync(
                            rep.FilePath,
                            (rep.Album ?? "").Trim(),
                            rep.AlbumArtist,
                            rep.Artist,
                            rep.Year,
                            null,
                            cancellationToken,
                            http)
                        .ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    failed += members.Count;
                    continue;
                }

                if (!fetch.Ok || fetch.ImageBytes is not { Length: > 0 } pic)
                {
                    skipped += members.Count;
                    continue;
                }

                foreach (var t in members)
                    embedJobs.Add((t, pic, fetch.MusicBrainzReleaseId));
            }

            var ok = 0;
            var embedTotal = embedJobs.Count;
            if (embedTotal == 0)
            {
                progress?.Report(new RemoteAlbumArtScanUiProgress(0, -1,
                    candidateList.Count == 0
                        ? "No files needed new embedded artwork."
                        : "Could not download artwork for any of the remaining albums."));
                progress?.Report(new RemoteAlbumArtScanUiProgress(0, -1, "Updating library index…"));
                await UpdateLibraryCacheAsync();
                await Dispatcher.InvokeAsync(() => RefreshAfterBatchMetadataEdit());
                return (ok, skipped, failed);
            }

            progress?.Report(new RemoteAlbumArtScanUiProgress(0, embedTotal,
                $"Writing embedded artwork to files… 0/{embedTotal}"));

            scanConcurrencySmoothed = 0;
            lastResourceSnapshot = null;
            batchesSinceSample = int.MaxValue;
            batchIndex = 0;
            var embedProgressDone = 0;

            foreach (var embedBatch in embedJobs.Chunk(batchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mustSample = batchIndex == 0 || batchesSinceSample >= 2;
                int dop;
                if (mustSample)
                {
                    var sampleInterval = lastResourceSnapshot.HasValue && lastResourceSnapshot.Value.CpuBusyPercent < 40
                        ? TimeSpan.FromMilliseconds(50)
                        : TimeSpan.FromMilliseconds(100);
                    lastResourceSnapshot = await Task.Run(
                            () => WindowsSystemMetrics.Sample(sampleInterval),
                            cancellationToken)
                        .ConfigureAwait(true);
                    dop = ScanConcurrencyAdvisor.Recommend(
                        lastResourceSnapshot.Value,
                        Environment.ProcessorCount,
                        ref scanConcurrencySmoothed);
                    batchesSinceSample = 0;
                }
                else
                    dop = scanConcurrencySmoothed;

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = dop,
                    CancellationToken = cancellationToken
                };

                await Parallel.ForEachAsync(embedBatch, parallelOptions, async (job, ct) =>
                {
                    try
                    {
                        ct.ThrowIfCancellationRequested();
                        var track = job.Track;
                        var path = track.FilePath;
                        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                        {
                            Interlocked.Increment(ref failed);
                            return;
                        }

                        var picCopy = (byte[])job.Pic.Clone();
                        var release = await Dispatcher.InvokeAsync(
                            () => ReleasePlaybackForMetadataWrite(path),
                            DispatcherPriority.Normal,
                            ct);

                        try
                        {
                            var saved = await Task.Run(
                                    () => TrackMetadataSaver.TrySaveEmbeddedCoverOnly(path, picCopy, job.Mbid, out _),
                                    ct)
                                .ConfigureAwait(false);
                            if (!saved)
                                Interlocked.Increment(ref failed);
                            else
                            {
                                Interlocked.Increment(ref ok);
                                var albumKey = track.Album ?? "";
                                var artistKey = track.Artist ?? "";
                                await Task.Run(
                                        () =>
                                        {
                                            AlbumArtCacheManager.InvalidateAlbum(albumKey, artistKey);
                                            AlbumArtThumbnailHelper.InvalidateFullSizeCache(path);
                                            TrackMetadataLoader.ReloadTagFieldsFromFile(track);
                                        },
                                        ct)
                                    .ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            await Dispatcher.InvokeAsync(
                                () => RestorePlaybackAfterMetadataWrite(release),
                                DispatcherPriority.Normal,
                                ct);
                        }
                    }
                    finally
                    {
                        var done = Interlocked.Increment(ref embedProgressDone);
                        if (done % progressEvery == 0 || done == embedTotal)
                        {
                            progress?.Report(new RemoteAlbumArtScanUiProgress(done, embedTotal,
                                $"Writing embedded artwork to files… {done}/{embedTotal}"));
                        }
                    }
                });

                if (!mustSample)
                    batchesSinceSample++;
                batchIndex++;
            }

            progress?.Report(new RemoteAlbumArtScanUiProgress(embedTotal, embedTotal,
                $"Writing embedded artwork to files… {embedTotal}/{embedTotal}"));

            progress?.Report(new RemoteAlbumArtScanUiProgress(0, -1, "Updating library index…"));
            await UpdateLibraryCacheAsync();
            await Dispatcher.InvokeAsync(() => RefreshAfterBatchMetadataEdit());
            return (ok, skipped, failed);
        }

        private async Task<(int Ok, int Skipped, int Failed)> ApplyFruitAppStorefrontArtToLibraryAsync(
            IProgress<(int done, int total)>? progress,
            CancellationToken cancellationToken)
        {
            var list = allTracks.ToList();
            var fetchCache = new ConcurrentDictionary<string, byte[]?>(StringComparer.Ordinal);
            using var fetchSerialize = new SemaphoreSlim(1, 1);
            var networkRequestDone = false;

            int ok = 0, skipped = 0, failed = 0;
            var progressDone = 0;
            var total = list.Count;
            const int storefrontBatchSize = 320;
            const int storefrontProgressEvery = 22;

            using var fruitAppHttp = AlbumArtRemoteFetcher.CreateFruitAppSearchHttpClient();

            if (total == 0)
            {
                await UpdateLibraryCacheAsync();
                await Dispatcher.InvokeAsync(() => RefreshAfterBatchMetadataEdit());
                return (0, 0, 0);
            }

            int scanConcurrencySmoothed = 0;
            SystemResourceSnapshot? lastResourceSnapshot = null;
            var batchesSinceSample = int.MaxValue;
            var batchIndex = 0;

            foreach (var batch in list.Chunk(storefrontBatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mustSample = batchIndex == 0 || batchesSinceSample >= 2;
                int dop;
                if (mustSample)
                {
                    var sampleInterval = lastResourceSnapshot.HasValue && lastResourceSnapshot.Value.CpuBusyPercent < 40
                        ? TimeSpan.FromMilliseconds(50)
                        : TimeSpan.FromMilliseconds(100);
                    lastResourceSnapshot = await Task.Run(
                            () => WindowsSystemMetrics.Sample(sampleInterval),
                            cancellationToken)
                        .ConfigureAwait(true);
                    dop = ScanConcurrencyAdvisor.Recommend(
                        lastResourceSnapshot.Value,
                        Environment.ProcessorCount,
                        ref scanConcurrencySmoothed);
                    batchesSinceSample = 0;
                }
                else
                    dop = scanConcurrencySmoothed;

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = dop,
                    CancellationToken = cancellationToken
                };

                await Parallel.ForEachAsync(batch, parallelOptions, async (track, ct) =>
                {
                    try
                    {
                        ct.ThrowIfCancellationRequested();
                        var path = track.FilePath;
                        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                        {
                            Interlocked.Increment(ref failed);
                            return;
                        }

                        if (!TryStorefrontAlbumKey(track, out var groupKey))
                        {
                            Interlocked.Increment(ref skipped);
                            return;
                        }

                        bool skipForEmbed;
                        try
                        {
                            skipForEmbed = await Task.Run(
                                    () => EmbeddedCoverEligibility.ShouldSkipSystemCacheEmbed(path),
                                    ct)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch
                        {
                            Interlocked.Increment(ref failed);
                            return;
                        }

                        if (skipForEmbed)
                        {
                            Interlocked.Increment(ref skipped);
                            return;
                        }

                        byte[]? pic;
                        if (!fetchCache.TryGetValue(groupKey, out pic))
                        {
                            await fetchSerialize.WaitAsync(ct).ConfigureAwait(false);
                            try
                            {
                                ct.ThrowIfCancellationRequested();
                                if (!fetchCache.TryGetValue(groupKey, out pic))
                                {
                                    if (networkRequestDone)
                                        await Task.Delay(FruitAppStorefrontBatchDelayMs, ct).ConfigureAwait(false);
                                    try
                                    {
                                        pic = await AlbumArtRemoteFetcher.TryFetchFruitAppStorefrontCoverBytesAsync(
                                                fruitAppHttp,
                                                (track.Album ?? "").Trim(),
                                                track.AlbumArtist,
                                                track.Artist,
                                                ct)
                                            .ConfigureAwait(false);
                                    }
                                    catch (OperationCanceledException)
                                    {
                                        throw;
                                    }
                                    catch
                                    {
                                        pic = null;
                                    }

                                    fetchCache[groupKey] = pic;
                                    networkRequestDone = true;
                                }
                            }
                            finally
                            {
                                fetchSerialize.Release();
                            }
                        }

                        if (pic == null || pic.Length == 0)
                        {
                            Interlocked.Increment(ref skipped);
                            return;
                        }

                        var picCopy = (byte[])pic.Clone();
                        var release = await Dispatcher.InvokeAsync(
                            () => ReleasePlaybackForMetadataWrite(path),
                            DispatcherPriority.Normal,
                            ct);

                        try
                        {
                            var saved = await Task.Run(
                                    () => TrackMetadataSaver.TrySaveEmbeddedCoverOnly(path, picCopy, null, out _),
                                    ct)
                                .ConfigureAwait(false);
                            if (!saved)
                                Interlocked.Increment(ref failed);
                            else
                            {
                                Interlocked.Increment(ref ok);
                                var albumKey = track.Album ?? "";
                                var artistKey = track.Artist ?? "";
                                await Task.Run(
                                        () =>
                                        {
                                            AlbumArtCacheManager.InvalidateAlbum(albumKey, artistKey);
                                            AlbumArtThumbnailHelper.InvalidateFullSizeCache(path);
                                            TrackMetadataLoader.ReloadTagFieldsFromFile(track);
                                        },
                                        ct)
                                    .ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            await Dispatcher.InvokeAsync(
                                () => RestorePlaybackAfterMetadataWrite(release),
                                DispatcherPriority.Normal,
                                ct);
                        }
                    }
                    finally
                    {
                        var done = Interlocked.Increment(ref progressDone);
                        if (done % storefrontProgressEvery == 0 || done == total)
                            progress?.Report((done, total));
                    }
                });

                if (!mustSample)
                    batchesSinceSample++;
                batchIndex++;
            }

            progress?.Report((total, total));

            await UpdateLibraryCacheAsync();
            await Dispatcher.InvokeAsync(() => RefreshAfterBatchMetadataEdit());
            return (ok, skipped, failed);
        }
    }
}
