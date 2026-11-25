using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using musicApp.Helpers;

namespace musicApp
{
    /// <summary>
    /// iTunes-style "Automatically Add" folder: audio dropped there is moved into the organized library folder and
    /// added; anything else goes to "Not Added\&lt;timestamp&gt;". Watcher events only trigger a full rescan of
    /// the folder, so lost or duplicate events do not matter.
    /// </summary>
    public partial class MainWindow
    {
        private const string NotAddedFolderName = "Not Added";
        private static readonly TimeSpan AutoAddQuietPeriod = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan AutoAddBusyRetry = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan AutoAddITunesRetry = TimeSpan.FromSeconds(30);

        private readonly SemaphoreSlim _autoAddGate = new(1, 1);
        private readonly CancellationTokenSource _autoAddCts = new();
        private FileSystemWatcher? _autoAddWatcher;
        private DispatcherTimer? _autoAddTimer;

        private sealed record AutoAddRun(List<Song> Added, bool Busy);

        /// <summary>Starts, restarts, or stops the drop-folder watcher to match the saved preferences.</summary>
        public void ApplyAutoAddPreferences()
        {
            var folder = GetAutoAddFolder();
            if (folder != null && _autoAddWatcher != null && LibraryPathHelper.PathsEqual(folder, _autoAddWatcher.Path))
                return;

            StopAutoAddWatcher();
            if (folder == null)
                return;

            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size |
                                   NotifyFilters.LastWrite
                };
                FileSystemEventHandler onChange = (_, e) => OnAutoAddFolderEvent(folder, e.FullPath);
                watcher.Created += onChange;
                watcher.Changed += onChange;
                watcher.Renamed += (_, e) => OnAutoAddFolderEvent(folder, e.FullPath);
                // Buffer overflow: some events were lost, rescan anyway. Folder or drive gone: stop until next launch.
                watcher.Error += (_, _) => Dispatcher.InvokeAsync(() =>
                {
                    if (!ReferenceEquals(_autoAddWatcher, watcher))
                        return; // a newer watcher replaced this one before the error got here
                    if (Directory.Exists(folder))
                        ScheduleAutoAdd(AutoAddQuietPeriod);
                    else
                        StopAutoAddWatcher();
                });
                watcher.EnableRaisingEvents = true;
                _autoAddWatcher = watcher;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Auto-add watcher failed for {folder}: {ex.Message}");
                return;
            }

            ScheduleAutoAdd(AutoAddQuietPeriod);
        }

        private static string? GetAutoAddFolder()
        {
            var library = PreferencesManager.Instance.LoadPreferencesSync().Library;
            if (!library.AutoAddEnabled || string.IsNullOrWhiteSpace(library.AutoAddFolder))
                return null;
            var folder = LibraryPathHelper.TryNormalizePath(library.AutoAddFolder);
            return folder != null && Directory.Exists(folder) ? folder : null;
        }

        private void StopAutoAddWatcher()
        {
            _autoAddTimer?.Stop();
            _autoAddWatcher?.Dispose();
            _autoAddWatcher = null;
        }

        private void OnAutoAddFolderEvent(string dropFolder, string path)
        {
            if (LibraryPathHelper.IsFolderUnderOrEqual(path, Path.Combine(dropFolder, NotAddedFolderName)))
                return;
            Dispatcher.InvokeAsync(() => ScheduleAutoAdd(AutoAddQuietPeriod));
        }

        // Debounce: every call restarts the timer, so a folder copy runs once after it goes quiet. UI thread only.
        private void ScheduleAutoAdd(TimeSpan delay)
        {
            if (_autoAddCts.IsCancellationRequested)
                return;
            var timer = _autoAddTimer;
            if (timer == null)
            {
                timer = _autoAddTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
                timer.Tick += async (_, _) =>
                {
                    timer.Stop();
                    await ProcessDropFolderAsync();
                };
            }
            timer.Stop();
            timer.Interval = delay;
            timer.Start();
        }

        /// <summary>One pass over the drop folder. Never throws; runs are serialized.</summary>
        private async Task ProcessDropFolderAsync()
        {
            var token = _autoAddCts.Token;
            try
            {
                await _autoAddGate.WaitAsync(token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            StatusProgressCoordinator.Phase? ownedPhase = null;
            try
            {
                // Read after the gate: a run queued before the user turned the feature off must not move anything.
                var dropFolder = GetAutoAddFolder();
                if (dropFolder == null)
                    return;

                // Both apps moving the same files would race. Check back later in case the folder is not
                // iTunes' own and nothing else will trigger a run after iTunes closes.
                if (IsITunesRunning())
                {
                    ScheduleAutoAdd(AutoAddITunesRetry);
                    return;
                }

                var musicFolders = await libraryManager.GetMusicFoldersAsync();
                var destinationRoot = AutoAddFolderLocator.DestinationRoot(dropFolder, musicFolders);
                if (destinationRoot == null)
                    return;

                var files = await Task.Run(() => ListDropFolderFiles(dropFolder), token);
                if (files.Count == 0)
                    return;

                if (_libraryPipelinePhase == null)
                    _libraryPipelinePhase = ownedPhase =
                        statusProgress.Begin(100, ("adding songs", 0.7), ("album artwork", 0.3));
                var phase = _libraryPipelinePhase;

                var run = await Task.Run(
                    () => ImportDropFolderFiles(dropFolder, destinationRoot, files, (done, total) =>
                        phase?.Report(0, done, total), token),
                    CancellationToken.None);

                // Another library flow may already have registered a moved file (reconcile, rescan).
                var added = run.Added.Where(s => TryRegisterLibraryPath(s.FilePath)).ToList();
                if (added.Count > 0)
                    await CommitLibraryChangesAsync(added, Array.Empty<Song>(), runArtPass: true);

                await Task.Run(() => DeleteEmptySubfolders(dropFolder));

                if (run.Busy && !token.IsCancellationRequested)
                    ScheduleAutoAdd(AutoAddBusyRetry);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Auto-add failed: {ex.Message}");
            }
            finally
            {
                ownedPhase?.Dispose();
                // Another flow may have taken the field since; only clear our own phase.
                if (ownedPhase != null && ReferenceEquals(_libraryPipelinePhase, ownedPhase))
                    _libraryPipelinePhase = null;
                _autoAddGate.Release();
            }
        }

        private static bool IsITunesRunning()
        {
            foreach (var name in new[] { "iTunes", "AppleMusic" })
            {
                var processes = Process.GetProcessesByName(name);
                var running = processes.Length > 0;
                foreach (var p in processes)
                    p.Dispose();
                if (running)
                    return true;
            }
            return false;
        }

        // Everything under the drop folder except "Not Added" and OS/download leftovers, which stay where they are.
        private static List<string> ListDropFolderFiles(string dropFolder)
        {
            var notAdded = Path.Combine(dropFolder, NotAddedFolderName);
            return Directory.EnumerateFiles(dropFolder, "*", DropFolderEnumeration)
                .Where(f => !LibraryPathHelper.IsFolderUnderOrEqual(f, notAdded) && !IsIgnoredDropFile(f))
                .ToList();
        }

        // Hidden/system files stay put. Junctions are never followed: one pointing into the library would
        // otherwise move songs that are already there.
        private static readonly EnumerationOptions DropFolderEnumeration = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System
        };

        private static bool IsIgnoredDropFile(string path)
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith("~$", StringComparison.Ordinal) || name.StartsWith("._", StringComparison.Ordinal))
                return true;
            if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) ||
                name.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase))
                return true;
            var ext = Path.GetExtension(name);
            return ext.Equals(".tmp", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".part", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".crdownload", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Moves each ready file: audio that reads into the organized library, the rest into Not Added. Files still
        /// being written (cannot be opened exclusively) are left for a later run. Background thread; touches no UI.
        /// </summary>
        private static AutoAddRun ImportDropFolderFiles(
            string dropFolder, string destinationRoot, List<string> files, Action<int, int> report,
            CancellationToken token)
        {
            var added = new List<Song>();
            var busy = false;
            string? notAddedRunFolder = null;

            for (var i = 0; i < files.Count && !token.IsCancellationRequested; i++)
            {
                var file = files[i];
                report(i, files.Count);
                try
                {
                    if (!IsReadyToMove(file))
                    {
                        busy = true;
                        continue;
                    }

                    var song = LibraryPathHelper.IsSupportedAudioFile(file) ? TrackMetadataLoader.LoadSong(file) : null;
                    // LoadSong still returns a song for an unreadable file; no duration means no audio was read.
                    if (song == null || song.DurationTimeSpan <= TimeSpan.Zero)
                    {
                        notAddedRunFolder ??= Path.Combine(dropFolder, NotAddedFolderName,
                            DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss"));
                        MoveFile(file, Path.Combine(notAddedRunFolder, Path.GetRelativePath(dropFolder, file)));
                        continue;
                    }

                    var target = Path.Combine(destinationRoot,
                        ITunesMediaPathBuilder.BuildRelativePath(song, Path.GetExtension(file)));
                    song.FilePath = MoveFile(file, target);
                    song.DateAdded = DateTime.Now;
                    added.Add(song);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Locked or vanished between listing and moving: try again on the next run.
                    Debug.WriteLine($"Auto-add skipped {file}: {ex.Message}");
                    busy = true;
                }
            }

            report(files.Count, files.Count);
            return new AutoAddRun(added, busy);
        }

        // A file still being copied or downloaded is open for writing, so an exclusive open fails.
        private static bool IsReadyToMove(string path)
        {
            try
            {
                using var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
        }

        // Duplicates get " 1", " 2"... like iTunes. Returns the path the file ended up at.
        private static string MoveFile(string source, string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            target = ITunesMediaPathBuilder.ResolveCollision(target);
            File.Move(source, target);
            return target;
        }

        // Bottom-up so nested empty album folders go too. Non-recursive delete only removes truly empty folders.
        private static void DeleteEmptySubfolders(string dropFolder)
        {
            var notAdded = Path.Combine(dropFolder, NotAddedFolderName);
            try
            {
                var dirs = Directory.EnumerateDirectories(dropFolder, "*", new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint
                    })
                    .Where(d => !LibraryPathHelper.IsFolderUnderOrEqual(d, notAdded))
                    .OrderByDescending(d => d.Length);
                foreach (var dir in dirs)
                {
                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(dir).Any())
                            Directory.Delete(dir);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Auto-add cleanup failed: {ex.Message}");
            }
        }

        private void ShutdownAutoAdd()
        {
            _autoAddCts.Cancel();
            StopAutoAddWatcher();
        }
    }
}
