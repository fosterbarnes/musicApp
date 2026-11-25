using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using musicApp.Helpers;

namespace musicApp
{
    public class LibraryManager
    {
        private static readonly string AppDataPath = AppPaths.AppDataDirectory;

        public static string SettingsDirectoryPath => AppDataPath;
        
        private static readonly string LibraryCacheFilePath = Path.Combine(AppDataPath, "library.json");
        private static readonly string RecentlyPlayedFilePath = Path.Combine(AppDataPath, "recentlyPlayed.json");
        private static readonly string LibraryFoldersFilePath = Path.Combine(AppDataPath, "libraryFolders.json");
        private static readonly string PlaylistsFilePath = Path.Combine(AppDataPath, "playlists.json");

        public class LibraryCache
        {
            public List<Song> Tracks { get; set; } = new List<Song>();

            /// <summary>
            /// False in files saved before Date Added came from the file's creation time; startup then moves each
            /// date back to the file's date once (MainWindow.LoadMusicFromCacheAsync). Every save writes true.
            /// </summary>
            public bool DateAddedFromFiles { get; set; }
        }

        public class LibraryFolders
        {
            public List<string> MusicFolders { get; set; } = new List<string>();
            public Dictionary<string, DateTime> FolderLastScanned { get; set; } = new Dictionary<string, DateTime>();
        }

        public class RecentlyPlayedCache
        {
            public List<RecentlyPlayedItem> RecentlyPlayed { get; set; } = new List<RecentlyPlayedItem>();
        }

        public class RecentlyPlayedItem
        {
            public string FilePath { get; set; } = "";
            public DateTime LastPlayed { get; set; } = DateTime.Now;
        }

        public class PlaylistsCache
        {
            public List<Playlist> Playlists { get; set; } = new List<Playlist>();
        }

        private static LibraryManager? _instance;
        private static readonly object _lock = new object();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> FileGates =
            new(StringComparer.OrdinalIgnoreCase);

        public static LibraryManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new LibraryManager();
                    }
                }
                return _instance;
            }
        }

        private LibraryManager()
        {
            AppPaths.EnsureAppDataDirectory();
        }

        // Write to a temp file, then atomically swap it in. A crash mid-save can
        // never corrupt the existing cache; the old file stays last-known-good.
        private static async Task SaveJsonAtomicAsync<T>(
            string path, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        {
            var tempPath = path + ".tmp";
            var gate = FileGate(path);
            await gate.WaitAsync();
            try
            {
                using (var stream = File.Create(tempPath))
                    await JsonSerializer.SerializeAsync(stream, value, typeInfo);
                File.Move(tempPath, path, overwrite: true);
            }
            finally
            {
                gate.Release();
            }
        }

        // Loads take the same per-file gate as saves: a save's File.Move cannot replace a file a load still
        // has open (it fails even with FileShare.Delete), and that failure was swallowed, losing the save.
        // Per file, so the startup loads of different files still run in parallel.
        private static async Task<T?> LoadJsonAsync<T>(
            string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) where T : class
        {
            var gate = FileGate(path);
            await gate.WaitAsync();
            try
            {
                if (!File.Exists(path))
                    return null;
                using var stream = File.OpenRead(path);
                return await JsonSerializer.DeserializeAsync(stream, typeInfo);
            }
            finally
            {
                gate.Release();
            }
        }

        private static SemaphoreSlim FileGate(string path) =>
            FileGates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));

        #region Library Cache Management

        public async Task<LibraryCache> LoadLibraryCacheAsync()
        {
            try
            {
                return await LoadJsonAsync(LibraryCacheFilePath, LibraryJsonContext.Default.LibraryCache)
                       ?? new LibraryCache();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading library cache: {ex.Message}");
            }
            return new LibraryCache();
        }

        public async Task SaveLibraryCacheAsync(LibraryCache cache)
        {
            try
            {
                await SaveJsonAtomicAsync(LibraryCacheFilePath, cache, LibraryJsonContext.Default.LibraryCache);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving library cache: {ex.Message}");
            }
        }

        #endregion

        #region Recently Played Management

        public async Task<RecentlyPlayedCache> LoadRecentlyPlayedAsync()
        {
            try
            {
                return await LoadJsonAsync(RecentlyPlayedFilePath, LibraryJsonContext.Default.RecentlyPlayedCache)
                       ?? new RecentlyPlayedCache();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading recently played: {ex.Message}");
            }
            return new RecentlyPlayedCache();
        }

        public async Task SaveRecentlyPlayedAsync(RecentlyPlayedCache recentlyPlayed)
        {
            try
            {
                await SaveJsonAtomicAsync(RecentlyPlayedFilePath, recentlyPlayed, LibraryJsonContext.Default.RecentlyPlayedCache);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving recently played: {ex.Message}");
            }
        }

        #endregion

        #region Library Folders Management

        public async Task<LibraryFolders> LoadLibraryFoldersAsync()
        {
            try
            {
                var result = await LoadJsonAsync(LibraryFoldersFilePath, LibraryJsonContext.Default.LibraryFolders);
                if (result != null)
                {
                    ApplyMusicFolderNormalization(result);
                    return result;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading library folders: {ex.Message}");
            }
            return new LibraryFolders();
        }

        private static void ApplyMusicFolderNormalization(LibraryFolders folders)
        {
            var raw = folders.MusicFolders ?? new List<string>();
            var collapsed = LibraryPathHelper.CollapseOverlappingMusicRoots(raw);
            var mergedScan = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in folders.FolderLastScanned)
            {
                var root = LibraryPathHelper.FindCanonicalMusicRoot(kv.Key, collapsed);
                if (root == null) continue;
                if (!mergedScan.TryGetValue(root, out var existing) || kv.Value > existing)
                    mergedScan[root] = kv.Value;
            }
            folders.MusicFolders = collapsed;
            folders.FolderLastScanned = mergedScan;
        }

        public async Task SaveLibraryFoldersAsync(LibraryFolders folders)
        {
            try
            {
                ApplyMusicFolderNormalization(folders);
                await SaveJsonAtomicAsync(LibraryFoldersFilePath, folders, LibraryJsonContext.Default.LibraryFolders);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving library folders: {ex.Message}");
            }
        }

        #endregion

        #region Playlists Management

        public async Task<PlaylistsCache> LoadPlaylistsAsync()
        {
            try
            {
                return await LoadJsonAsync(PlaylistsFilePath, LibraryJsonContext.Default.PlaylistsCache)
                       ?? new PlaylistsCache();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading playlists: {ex.Message}");
            }
            return new PlaylistsCache();
        }

        public async Task SavePlaylistsAsync(PlaylistsCache playlists)
        {
            try
            {
                await SaveJsonAtomicAsync(PlaylistsFilePath, playlists, LibraryJsonContext.Default.PlaylistsCache);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving playlists: {ex.Message}");
            }
        }

        /// <summary>
        /// Saves playlists from an enumerable collection of Playlist objects.
        /// </summary>
        public Task SavePlaylistsFromCollectionAsync(IEnumerable<Playlist> playlistsCollection)
        {
            var cache = new PlaylistsCache
            {
                Playlists = playlistsCollection?.ToList() ?? new List<Playlist>()
            };
            return SavePlaylistsAsync(cache);
        }

        /// <summary>
        /// Imports a playlist from an M3U file and returns a Playlist instance with TrackFilePaths populated.
        /// Caller is responsible for adding it to any collections and persisting via SavePlaylistsFromCollectionAsync.
        /// </summary>
        public Playlist ImportPlaylistFromM3u(string m3uPath, string playlistName, IEnumerable<Song> allSongs)
        {
            if (string.IsNullOrWhiteSpace(m3uPath))
                throw new ArgumentException("M3U path must not be null or empty.", nameof(m3uPath));
            if (string.IsNullOrWhiteSpace(playlistName))
                playlistName = Path.GetFileNameWithoutExtension(m3uPath);

            var playlist = new Playlist(playlistName);

            var availableTracks = allSongs?.ToList() ?? new List<Song>();
            var m3uDir = Path.GetDirectoryName(m3uPath);
            foreach (var entry in Helpers.M3uPlaylistHelper.Parse(m3uPath))
            {
                var path = entry.FilePath;
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                if (!Path.IsPathRooted(path) && !string.IsNullOrWhiteSpace(m3uDir))
                    path = Path.GetFullPath(Path.Combine(m3uDir, path));

                var match = availableTracks.FirstOrDefault(t => string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));
                // Store the library's spelling of the path so later removes, which compare paths, find it.
                playlist.TrackFilePaths.Add(match?.FilePath ?? path);
                if (match != null)
                {
                    playlist.Tracks.Add(match);
                }
            }

            playlist.LastModified = DateTime.Now;
            return playlist;
        }

        /// <summary>
        /// Exports the given playlist to an M3U file.
        /// </summary>
        public void ExportPlaylistToM3u(Playlist playlist, string outputPath)
        {
            if (playlist == null)
                throw new ArgumentNullException(nameof(playlist));
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Output path must not be null or empty.", nameof(outputPath));

            // Prefer the in-memory Tracks collection; fall back to TrackFilePaths if needed
            var tracks = new List<Song>();
            if (playlist.Tracks != null && playlist.Tracks.Count > 0)
            {
                tracks.AddRange(playlist.Tracks);
            }
            else if (playlist.TrackFilePaths != null && playlist.TrackFilePaths.Count > 0)
            {
                foreach (var path in playlist.TrackFilePaths)
                {
                    if (string.IsNullOrWhiteSpace(path))
                        continue;

                    tracks.Add(new Song
                    {
                        FilePath = path,
                        Title = Path.GetFileNameWithoutExtension(path)
                    });
                }
            }

            Helpers.M3uPlaylistHelper.Write(outputPath, tracks);
        }

        /// <summary>
        /// Adds a new playlist to an in-memory collection. Caller is responsible for persisting via SavePlaylistsFromCollectionAsync.
        /// </summary>
        public void AddPlaylist(ICollection<Playlist> playlistsCollection, Playlist playlist)
        {
            if (playlistsCollection == null || playlist == null)
                return;

            playlistsCollection.Add(playlist);
        }

        /// <summary>
        /// Deletes an existing playlist from an in-memory collection. Caller is responsible for persisting via SavePlaylistsFromCollectionAsync.
        /// </summary>
        public void DeletePlaylist(ICollection<Playlist> playlistsCollection, Playlist playlist)
        {
            if (playlistsCollection == null || playlist == null)
                return;

            if (!playlistsCollection.Remove(playlist))
            {
                var toRemove = playlistsCollection.FirstOrDefault(p => string.Equals(p.Name, playlist.Name, StringComparison.OrdinalIgnoreCase));
                if (toRemove != null)
                    playlistsCollection.Remove(toRemove);
            }
        }

        /// <summary>
        /// Renames an existing playlist in-memory. Caller is responsible for persisting via SavePlaylistsFromCollectionAsync.
        /// </summary>
        public void RenamePlaylist(Playlist playlist, string newName)
        {
            if (playlist == null || string.IsNullOrWhiteSpace(newName))
                return;

            playlist.Name = newName;
            playlist.LastModified = DateTime.Now;
        }

        /// <summary>
        /// Adds a track to the given playlist (in-memory). Caller is responsible for persisting via SavePlaylistsFromCollectionAsync.
        /// </summary>
        public void AddTrackToPlaylist(Playlist playlist, Song track)
        {
            if (playlist == null || track == null)
                return;

            playlist.AddTrack(track);
        }

        /// <summary>
        /// Removes a track from the given playlist (in-memory). Caller is responsible for persisting via SavePlaylistsFromCollectionAsync.
        /// </summary>
        public void RemoveTrackFromPlaylist(Playlist playlist, Song track)
        {
            if (playlist == null || track == null)
                return;

            playlist.RemoveTrack(track);
        }

        #endregion

        #region Music Folders Management

        public async Task<List<string>> GetMusicFoldersAsync()
        {
            var folders = await LoadLibraryFoldersAsync();
            return folders.MusicFolders ?? new List<string>();
        }

        private static readonly SemaphoreSlim FoldersUpdateGate = new(1, 1);

        // One load-change-save at a time: a folder add and a scan-time stamp used to load the same old copy,
        // and whichever saved last dropped the other's change. mutate returns false when nothing changed.
        private async Task UpdateLibraryFoldersAsync(Func<LibraryFolders, bool> mutate)
        {
            await FoldersUpdateGate.WaitAsync();
            try
            {
                var folders = await LoadLibraryFoldersAsync();
                if (mutate(folders))
                    await SaveLibraryFoldersAsync(folders);
            }
            finally
            {
                FoldersUpdateGate.Release();
            }
        }

        public Task AddMusicFolderAsync(string folderPath)
        {
            var normalized = LibraryPathHelper.TryNormalizePath(folderPath);
            if (normalized == null) return Task.CompletedTask;
            return UpdateLibraryFoldersAsync(folders =>
            {
                if (folders.MusicFolders.Any(f => LibraryPathHelper.PathsEqual(f, normalized))) return false;
                folders.MusicFolders.Add(normalized);
                return true;
            });
        }

        public Task RemoveMusicFolderAsync(string folderPath) =>
            UpdateLibraryFoldersAsync(folders =>
            {
                var match = folders.MusicFolders.FirstOrDefault(f => LibraryPathHelper.PathsEqual(f, folderPath));
                return match != null && folders.MusicFolders.Remove(match);
            });

        #endregion

        #region Utility Methods

        public Task<bool> HasNewFilesInFolderAsync(string folderPath) =>
            HasNewFilesInFolderAsync(folderPath, null);

        public async Task<bool> HasNewFilesInFolderAsync(string folderPath, LibraryFolders? preloaded)
        {
            try
            {
                var normalized = LibraryPathHelper.TryNormalizePath(folderPath);
                if (normalized == null) return true;

                var folders = preloaded ?? await LoadLibraryFoldersAsync();
                DateTime? lastScanned = null;
                foreach (var kv in folders.FolderLastScanned)
                {
                    if (LibraryPathHelper.PathsEqual(kv.Key, normalized))
                    {
                        lastScanned = kv.Value;
                        break;
                    }
                }
                if (lastScanned == null) return true;

                return await Task.Run(() =>
                {
                    var dirInfo = new DirectoryInfo(normalized);
                    if (!dirInfo.Exists) return true;

                    // fast check: root folder mtime updated on file add/delete
                    if (dirInfo.LastWriteTime > lastScanned.Value)
                        return true;

                    foreach (var file in dirInfo.EnumerateFiles("*.*", SearchOption.AllDirectories))
                    {
                        if (!LibraryPathHelper.IsSupportedAudioFile(file.Name)) continue;
                        if (file.LastWriteTime > lastScanned.Value) return true;
                    }
                    return false;
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking for new files: {ex.Message}");
                return true;
            }
        }

        public async Task UpdateFolderScanTimeAsync(string folderPath)
        {
            try
            {
                var normalized = LibraryPathHelper.TryNormalizePath(folderPath);
                if (normalized == null) return;
                await UpdateLibraryFoldersAsync(folders =>
                {
                    folders.FolderLastScanned[normalized] = DateTime.Now;
                    return true;
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating folder scan time: {ex.Message}");
            }
        }

        public async Task RemoveFolderFromCacheAsync(string folderPath)
        {
            try
            {
                var folders = await LoadLibraryFoldersAsync();
                var key = folders.FolderLastScanned.Keys.FirstOrDefault(k => LibraryPathHelper.PathsEqual(k, folderPath));
                if (key != null)
                    folders.FolderLastScanned.Remove(key);
                await SaveLibraryFoldersAsync(folders);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error removing folder from cache: {ex.Message}");
            }
        }

        #endregion
    }
}
