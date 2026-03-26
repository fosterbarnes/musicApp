using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using musicApp.Helpers;

namespace musicApp
{
    public class LibraryManager
    {
        private static readonly string AppDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            "musicApp");

        public static string SettingsDirectoryPath => AppDataPath;
        
        private static readonly string LibraryCacheFilePath = Path.Combine(AppDataPath, "library.json");
        private static readonly string RecentlyPlayedFilePath = Path.Combine(AppDataPath, "recentlyPlayed.json");
        private static readonly string LibraryFoldersFilePath = Path.Combine(AppDataPath, "libraryFolders.json");
        private static readonly string PlaylistsFilePath = Path.Combine(AppDataPath, "playlists.json");

        public class LibraryCache
        {
            public List<Song> Tracks { get; set; } = new List<Song>();
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
            EnsureAppDataDirectoryExists();
        }

        private void EnsureAppDataDirectoryExists()
        {
            if (!Directory.Exists(AppDataPath))
            {
                Directory.CreateDirectory(AppDataPath);
            }
        }

        #region Library Cache Management

        public async Task<LibraryCache> LoadLibraryCacheAsync()
        {
            try
            {
                if (File.Exists(LibraryCacheFilePath))
                {
                    var json = await File.ReadAllTextAsync(LibraryCacheFilePath);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    var cache = JsonSerializer.Deserialize<LibraryCache>(json, options);
                    return cache ?? new LibraryCache();
                }
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
                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                var json = JsonSerializer.Serialize(cache, options);
                await File.WriteAllTextAsync(LibraryCacheFilePath, json);
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
                if (File.Exists(RecentlyPlayedFilePath))
                {
                    var json = await File.ReadAllTextAsync(RecentlyPlayedFilePath);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    var recentlyPlayed = JsonSerializer.Deserialize<RecentlyPlayedCache>(json, options);
                    return recentlyPlayed ?? new RecentlyPlayedCache();
                }
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
                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                var json = JsonSerializer.Serialize(recentlyPlayed, options);
                await File.WriteAllTextAsync(RecentlyPlayedFilePath, json);
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
                if (File.Exists(LibraryFoldersFilePath))
                {
                    var json = await File.ReadAllTextAsync(LibraryFoldersFilePath);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    var folders = JsonSerializer.Deserialize<LibraryFolders>(json, options);
                    var result = folders ?? new LibraryFolders();
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
                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                var json = JsonSerializer.Serialize(folders, options);
                await File.WriteAllTextAsync(LibraryFoldersFilePath, json);
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
                if (File.Exists(PlaylistsFilePath))
                {
                    var json = await File.ReadAllTextAsync(PlaylistsFilePath);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    var playlists = JsonSerializer.Deserialize<PlaylistsCache>(json, options);
                    return playlists ?? new PlaylistsCache();
                }
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
                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                var json = JsonSerializer.Serialize(playlists, options);
                await File.WriteAllTextAsync(PlaylistsFilePath, json);
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
            foreach (var entry in Helpers.M3uPlaylistHelper.Parse(m3uPath))
            {
                var path = entry.FilePath;
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                playlist.TrackFilePaths.Add(path);

                var match = availableTracks.FirstOrDefault(t => string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));
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

        public async Task AddMusicFolderAsync(string folderPath)
        {
            var normalized = LibraryPathHelper.TryNormalizePath(folderPath);
            if (normalized == null) return;
            var folders = await LoadLibraryFoldersAsync();
            if (folders.MusicFolders.Any(f => LibraryPathHelper.PathsEqual(f, normalized))) return;
            folders.MusicFolders.Add(normalized);
            await SaveLibraryFoldersAsync(folders);
        }

        public async Task RemoveMusicFolderAsync(string folderPath)
        {
            var folders = await LoadLibraryFoldersAsync();
            var match = folders.MusicFolders.FirstOrDefault(f => LibraryPathHelper.PathsEqual(f, folderPath));
            if (match == null) return;
            folders.MusicFolders.Remove(match);
            await SaveLibraryFoldersAsync(folders);
        }

        #endregion

        #region Utility Methods

        public async Task<bool> HasNewFilesInFolderAsync(string folderPath)
        {
            try
            {
                var normalized = LibraryPathHelper.TryNormalizePath(folderPath);
                if (normalized == null) return true;

                var folders = await LoadLibraryFoldersAsync();
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

                var directoryInfo = new DirectoryInfo(normalized);
                var supportedExtensions = new[] { ".mp3", ".wav", ".flac", ".m4a", ".aac" };
                foreach (var file in directoryInfo.EnumerateFiles("*.*", SearchOption.AllDirectories))
                {
                    if (!supportedExtensions.Contains(file.Extension.ToLower())) continue;
                    if (file.LastWriteTime > lastScanned.Value) return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking for new files: {ex.Message}");
                return true; // Assume there are new files if we can't check
            }
        }

        public async Task UpdateFolderScanTimeAsync(string folderPath)
        {
            try
            {
                var normalized = LibraryPathHelper.TryNormalizePath(folderPath);
                if (normalized == null) return;
                var folders = await LoadLibraryFoldersAsync();
                folders.FolderLastScanned[normalized] = DateTime.Now;
                await SaveLibraryFoldersAsync(folders);
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
