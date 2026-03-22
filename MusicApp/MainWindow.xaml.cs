using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Threading;
using NAudio.Wave;
using ATL;
using System.Threading.Tasks;
using System.Collections.Generic;
using MusicApp.Views;
using MusicApp.Helpers;
using MusicApp.Dialogs;
using MusicApp.Constants;

namespace MusicApp
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // ===========================================
        // WINDOW MANAGEMENT
        // ===========================================
        private WindowManager windowManager;

        // ===========================================
        // DATA COLLECTIONS
        // ===========================================
        private ObservableCollection<Song> allTracks = new ObservableCollection<Song>();
        private ObservableCollection<Song> filteredTracks = new ObservableCollection<Song>();
        private ObservableCollection<Song> shuffledTracks = new ObservableCollection<Song>();
        private ObservableCollection<Song>? contextualPlaybackQueue;
        private int contextualPlaybackIndex = -1;
        private ObservableCollection<Playlist> playlists = new ObservableCollection<Playlist>();
        private ObservableCollection<Song> recentlyPlayed = new ObservableCollection<Song>();

        /// <summary>Playlists pinned to the sidebar; used for the dynamic pinned section.</summary>
        public ObservableCollection<Playlist> PinnedPlaylists { get; } = new ObservableCollection<Playlist>();

        /// <summary>True when at least one playlist is pinned; drives visibility of the pinned section.</summary>
        public bool HasPinnedPlaylists => PinnedPlaylists.Count > 0;

        /// <summary>Exposes current playlists for context menu and other consumers.</summary>
        public IReadOnlyList<Playlist> Playlists => playlists;

        // ===========================================
        // SETTINGS AND PERSISTENCE
        // ===========================================
        private LibraryManager libraryManager = LibraryManager.Instance;
        private SettingsManager settingsManager = SettingsManager.Instance;
        private SettingsManager.AppSettings appSettings = new SettingsManager.AppSettings();
        private DispatcherTimer? sidebarWidthSaveTimer;
        private bool isSidebarWidthDirty = false;

        // ===========================================
        // AUDIO PLAYBACK STATE
        // ===========================================
        private WaveOutEvent? waveOut;
        private AudioFileReader? audioFileReader;
        private int currentTrackIndex = -1;
        private int currentShuffledIndex = -1;
        private Song? currentTrack;
        private bool isManuallyStopping = false; // Flag to prevent infinite loops during manual stops
        private bool isManualNavigation = false; // Flag to differentiate between natural progression and manual navigation

        // ===========================================
        // MODULAR VIEWS
        // ===========================================
        private SongsView? songsView;
        private QueueView? queueViewControl;
        private RecentlyPlayedView? recentlyPlayedViewControl;
        private ArtistGenreView? artistsViewControl;
        private ArtistGenreView? genresViewControl;
        private AlbumsView? albumsViewControl;
        private PlaylistsView? playlistsViewControl;

        // ===========================================
        // CONSTRUCTOR AND INITIALIZATION
        // ===========================================
        public MainWindow()
        {
            InitializeComponent();

            windowManager = new WindowManager(this, titleBarPlayer);

            windowManager.WindowStateChanged += WindowManager_WindowStateChanged;

            // Try to load settings synchronously to set the correct initial position
            try
            {
                var initialSettings = settingsManager.LoadSettingsSync();
                if (initialSettings?.WindowState != null)
                {
                    windowManager.SetInitialPosition(
                        initialSettings.WindowState.Left,
                        initialSettings.WindowState.Top,
                        initialSettings.WindowState.Width,
                        initialSettings.WindowState.Height
                    );

                    if (initialSettings.WindowState.SidebarWidth > 0)
                    {
                        sidebarColumn.Width = new GridLength(initialSettings.WindowState.SidebarWidth);
                    }

                    appSettings = initialSettings;
                    
                    // Columns are rebuilt after views are initialized.
                }
                else
                {
                    windowManager.SetInitialPosition(
                        UILayoutConstants.DefaultWindowLeft,
                        UILayoutConstants.DefaultWindowTop,
                        UILayoutConstants.DefaultWindowWidth,
                        UILayoutConstants.DefaultWindowHeight);
                }
            }
            catch
            {
                windowManager.SetInitialPosition(
                    UILayoutConstants.DefaultWindowLeft,
                    UILayoutConstants.DefaultWindowTop,
                    UILayoutConstants.DefaultWindowWidth,
                    UILayoutConstants.DefaultWindowHeight);
            }

            TrackListColumnConfig.Initialize();
            CreateViewsAndWirePlayback();
            SetupEventHandlers();
            DataContext = this;

            _ = LoadSavedDataAsync();

            windowManager.InitializeWindowState();

            SetupSidebarWidthTracking();
        }

        private void CreateViewsAndWirePlayback()
        {
            songsView = new SongsView();
            queueViewControl = new QueueView();
            recentlyPlayedViewControl = new RecentlyPlayedView();
            artistsViewControl = new ArtistGenreView { ViewName = "Artists" };
            genresViewControl = new ArtistGenreView { ViewName = "Genres" };
            albumsViewControl = new AlbumsView();
            playlistsViewControl = new PlaylistsView();

            void OnPlayTrackRequested(object? s, Song track) => PlayTrack(track, s);
            songsView.PlayTrackRequested += OnPlayTrackRequested;
            queueViewControl.PlayTrackRequested += OnPlayTrackRequested;
            recentlyPlayedViewControl.PlayTrackRequested += OnPlayTrackRequested;
            artistsViewControl.PlayTrackRequested += OnPlayTrackRequested;
            genresViewControl.PlayTrackRequested += OnPlayTrackRequested;
            albumsViewControl.PlayTrackRequested += OnPlayTrackRequested;
            playlistsViewControl.PlayTrackRequested += OnPlayTrackRequested;

            songsView.PlayNextRequested += OnPlayNextRequested;
            queueViewControl.PlayNextRequested += OnPlayNextRequested;
            recentlyPlayedViewControl.PlayNextRequested += OnPlayNextRequested;
            artistsViewControl.PlayNextRequested += OnPlayNextRequested;
            genresViewControl.PlayNextRequested += OnPlayNextRequested;
            albumsViewControl.PlayNextRequested += OnPlayNextRequested;
            playlistsViewControl.PlayNextRequested += OnPlayNextRequested;

            songsView.AddToQueueRequested += OnAddToQueueRequested;
            songsView.AddTrackToPlaylistRequested += OnAddTrackToPlaylistRequested;
            songsView.CreateNewPlaylistWithTrackRequested += OnCreateNewPlaylistWithTrackRequested;
            songsView.InfoRequested += OnInfoRequested;
            queueViewControl.AddToQueueRequested += OnAddToQueueRequested;
            queueViewControl.AddTrackToPlaylistRequested += OnAddTrackToPlaylistRequested;
            queueViewControl.CreateNewPlaylistWithTrackRequested += OnCreateNewPlaylistWithTrackRequested;
            queueViewControl.InfoRequested += OnInfoRequested;
            recentlyPlayedViewControl.AddToQueueRequested += OnAddToQueueRequested;
            recentlyPlayedViewControl.AddTrackToPlaylistRequested += OnAddTrackToPlaylistRequested;
            recentlyPlayedViewControl.CreateNewPlaylistWithTrackRequested += OnCreateNewPlaylistWithTrackRequested;
            recentlyPlayedViewControl.InfoRequested += OnInfoRequested;
            artistsViewControl.AddToQueueRequested += OnAddToQueueRequested;
            artistsViewControl.AddTrackToPlaylistRequested += OnAddTrackToPlaylistRequested;
            artistsViewControl.CreateNewPlaylistWithTrackRequested += OnCreateNewPlaylistWithTrackRequested;
            artistsViewControl.InfoRequested += OnInfoRequested;
            genresViewControl.AddToQueueRequested += OnAddToQueueRequested;
            genresViewControl.AddTrackToPlaylistRequested += OnAddTrackToPlaylistRequested;
            genresViewControl.CreateNewPlaylistWithTrackRequested += OnCreateNewPlaylistWithTrackRequested;
            genresViewControl.InfoRequested += OnInfoRequested;
            albumsViewControl.AddToQueueRequested += OnAddToQueueRequested;
            albumsViewControl.AddTrackToPlaylistRequested += OnAddTrackToPlaylistRequested;
            albumsViewControl.CreateNewPlaylistWithTrackRequested += OnCreateNewPlaylistWithTrackRequested;
            albumsViewControl.InfoRequested += OnInfoRequested;
            playlistsViewControl.AddToQueueRequested += OnAddToQueueRequested;
            playlistsViewControl.AddTrackToPlaylistRequested += OnAddTrackToPlaylistRequested;
            playlistsViewControl.CreateNewPlaylistWithTrackRequested += OnCreateNewPlaylistWithTrackRequested;
            playlistsViewControl.InfoRequested += OnInfoRequested;

            songsView.ShowInExplorerRequested += OnShowInExplorerRequested;
            queueViewControl.ShowInExplorerRequested += OnShowInExplorerRequested;
            recentlyPlayedViewControl.ShowInExplorerRequested += OnShowInExplorerRequested;
            artistsViewControl.ShowInExplorerRequested += OnShowInExplorerRequested;
            genresViewControl.ShowInExplorerRequested += OnShowInExplorerRequested;
            albumsViewControl.ShowInExplorerRequested += OnShowInExplorerRequested;
            playlistsViewControl.ShowInExplorerRequested += OnShowInExplorerRequested;
            songsView.ShowInArtistsRequested += OnShowInArtistsRequested;
            queueViewControl.ShowInArtistsRequested += OnShowInArtistsRequested;
            recentlyPlayedViewControl.ShowInArtistsRequested += OnShowInArtistsRequested;
            artistsViewControl.ShowInArtistsRequested += OnShowInArtistsRequested;
            genresViewControl.ShowInArtistsRequested += OnShowInArtistsRequested;
            albumsViewControl.ShowInArtistsRequested += OnShowInArtistsRequested;
            playlistsViewControl.ShowInArtistsRequested += OnShowInArtistsRequested;
            songsView.ShowInAlbumsRequested += OnShowInAlbumsRequested;
            queueViewControl.ShowInAlbumsRequested += OnShowInAlbumsRequested;
            recentlyPlayedViewControl.ShowInAlbumsRequested += OnShowInAlbumsRequested;
            artistsViewControl.ShowInAlbumsRequested += OnShowInAlbumsRequested;
            genresViewControl.ShowInAlbumsRequested += OnShowInAlbumsRequested;
            albumsViewControl.ShowInAlbumsRequested += OnShowInAlbumsRequested;
            playlistsViewControl.ShowInAlbumsRequested += OnShowInAlbumsRequested;
            songsView.ShowInQueueRequested += OnShowInQueueRequested;
            recentlyPlayedViewControl.ShowInQueueRequested += OnShowInQueueRequested;
            artistsViewControl.ShowInQueueRequested += OnShowInQueueRequested;
            genresViewControl.ShowInQueueRequested += OnShowInQueueRequested;
            albumsViewControl.ShowInQueueRequested += OnShowInQueueRequested;
            playlistsViewControl.ShowInQueueRequested += OnShowInQueueRequested;
            queueViewControl.ShowInSongsRequested += OnShowInSongsRequested;
            recentlyPlayedViewControl.ShowInSongsRequested += OnShowInSongsRequested;
            artistsViewControl.ShowInSongsRequested += OnShowInSongsRequested;
            genresViewControl.ShowInSongsRequested += OnShowInSongsRequested;
            albumsViewControl.ShowInSongsRequested += OnShowInSongsRequested;
            playlistsViewControl.ShowInSongsRequested += OnShowInSongsRequested;

            songsView.RemoveFromLibraryRequested += OnRemoveFromLibraryRequested;
            queueViewControl.RemoveFromLibraryRequested += OnRemoveFromLibraryRequested;
            recentlyPlayedViewControl.RemoveFromLibraryRequested += OnRemoveFromLibraryRequested;
            artistsViewControl.RemoveFromLibraryRequested += OnRemoveFromLibraryRequested;
            genresViewControl.RemoveFromLibraryRequested += OnRemoveFromLibraryRequested;
            albumsViewControl.RemoveFromLibraryRequested += OnRemoveFromLibraryRequested;
            playlistsViewControl.RemoveFromLibraryRequested += OnRemoveFromLibraryRequested;

            songsView.AddMusicFolderRequested += OnAddMusicFolderRequested;

            songsView.DeleteRequested += OnDeleteRequested;
            queueViewControl.DeleteRequested += OnDeleteRequested;
            recentlyPlayedViewControl.DeleteRequested += OnDeleteRequested;
            artistsViewControl.DeleteRequested += OnDeleteRequested;
            genresViewControl.DeleteRequested += OnDeleteRequested;
            albumsViewControl.DeleteRequested += OnDeleteRequested;
            playlistsViewControl.DeleteRequested += OnDeleteRequested;

            albumsViewControl.ArtistNavigationRequested += AlbumsView_ArtistNavigationRequested;
            albumsViewControl.GenreNavigationRequested += AlbumsView_GenreNavigationRequested;

            playlistsViewControl.CreatePlaylistRequested += PlaylistsViewControl_CreatePlaylistRequested;
            playlistsViewControl.ImportPlaylistRequested += PlaylistsViewControl_ImportPlaylistRequested;
            playlistsViewControl.ExportPlaylistRequested += PlaylistsViewControl_ExportPlaylistRequested;
            playlistsViewControl.DeletePlaylistRequested += PlaylistsViewControl_DeletePlaylistRequested;
            playlistsViewControl.PlaylistPinnedChanged += PlaylistsViewControl_PlaylistPinnedChanged;
            playlistsViewControl.RemoveFromPlaylistRequested += OnRemoveFromPlaylistRequested;

            contentHost.Content = songsView;
            SetSidebarNavActive(btnLibrary);
        }

        /// <summary>
        /// Loads all saved data from settings files
        /// </summary>
        private async Task LoadSavedDataAsync()
        {
            try
            {
                var libraryCache = await libraryManager.LoadLibraryCacheAsync();

                var recentlyPlayedCache = await libraryManager.LoadRecentlyPlayedAsync();

                var playlistsCache = await libraryManager.LoadPlaylistsAsync();

                await LoadMusicFromSavedFoldersAsync(libraryCache);

                RestorePlaylists(playlistsCache);

                // Sync pinned playlists for sidebar (so they appear in the menu on launch)
                foreach (var p in playlists)
                    if (p.IsPinned)
                        PinnedPlaylists.Add(p);
                OnPropertyChanged(nameof(HasPinnedPlaylists));

                RestoreRecentlyPlayed(recentlyPlayedCache);

                UpdateUI();

                if (titleBarPlayer.IsShuffleEnabled)
                {
                    RegenerateShuffledTracks();
                }
            }
            catch (Exception ex)
            {
                MessageDialog.Show(this, "Error", $"Error loading saved data: {ex.Message}", MessageDialog.Buttons.Ok);
            }
        }

        /// <summary>
        /// Restores window state from settings
        /// </summary>
        private void RestoreWindowState()
        {
            if (appSettings.WindowState != null)
            {
                windowManager.RestoreWindowState(
                    appSettings.WindowState.Width,
                    appSettings.WindowState.Height,
                    appSettings.WindowState.Left,
                    appSettings.WindowState.Top,
                    appSettings.WindowState.IsMaximized
                );

                if (appSettings.WindowState.SidebarWidth > 0)
                {
                    sidebarColumn.Width = new GridLength(appSettings.WindowState.SidebarWidth);
                }

                RebuildAllViewColumns();
            }
        }

        /// <summary>
        /// Loads music from previously saved folders
        /// </summary>
        private async Task LoadMusicFromSavedFoldersAsync(LibraryManager.LibraryCache? libraryCache = null)
        {
            var musicFolders = await libraryManager.GetMusicFoldersAsync();
            if (musicFolders == null || musicFolders.Count == 0)
                return;

            libraryCache ??= await libraryManager.LoadLibraryCacheAsync();

            foreach (var folderPath in musicFolders)
            {
                if (Directory.Exists(folderPath))
                {
                    bool hasNewFiles = await libraryManager.HasNewFilesInFolderAsync(folderPath);

                    if (hasNewFiles)
                    {
                        await LoadMusicFromFolderAsync(folderPath, true);
                    }
                    else
                    {
                        await LoadMusicFromCacheAsync(folderPath, libraryCache);
                    }
                }
            }
        }

        /// <summary>
        /// Loads music from cache for a specific folder
        /// </summary>
        private async Task LoadMusicFromCacheAsync(string folderPath, LibraryManager.LibraryCache? libraryCache = null)
        {
            try
            {
                libraryCache ??= await libraryManager.LoadLibraryCacheAsync();
                var cachedTracks = libraryCache.Tracks.Where(t => t.FilePath.StartsWith(folderPath)).ToList();

                foreach (var track in cachedTracks)
                {
                    if (File.Exists(track.FilePath))
                    {
                        if (string.IsNullOrEmpty(track.FileType))
                        {
                            var extension = Path.GetExtension(track.FilePath);
                            if (!string.IsNullOrEmpty(extension))
                            {
                                track.FileType = extension.TrimStart('.').ToUpper();
                            }
                        }
                        
                        if (string.IsNullOrEmpty(track.Bitrate) || string.IsNullOrEmpty(track.SampleRate))
                        {
                            try
                            {
                                var atlTrack = new ATL.Track(track.FilePath);
                                if (string.IsNullOrEmpty(track.Bitrate) && atlTrack.Bitrate > 0)
                                {
                                    track.Bitrate = $"{atlTrack.Bitrate} kbps";
                                }
                                if (string.IsNullOrEmpty(track.SampleRate) && atlTrack.SampleRate > 0)
                                {
                                    track.SampleRate = $"{atlTrack.SampleRate / 1000.0:F1} kHz";
                                }
                            }
                            catch
                            {
                                // If ATL fails, will try to calculate below
                            }
                        }
                        
                        // Try NAudio fallback for sample rate if still not set
                        if (string.IsNullOrEmpty(track.SampleRate))
                        {
                            try
                            {
                                using var audioFile = new AudioFileReader(track.FilePath);
                                var sampleRate = audioFile.WaveFormat.SampleRate;
                                if (sampleRate > 0)
                                {
                                    track.SampleRate = $"{sampleRate / 1000.0:F1} kHz";
                                }
                            }
                            catch
                            {
                                // Ignore if NAudio fails
                            }
                        }
                        
                        // Restore DurationTimeSpan from Duration string (since it's not serialized)
                        if (track.DurationTimeSpan == TimeSpan.Zero && !string.IsNullOrEmpty(track.Duration))
                        {
                            try
                            {
                                // Parse Duration string (format: "mm:ss")
                                var parts = track.Duration.Split(':');
                                if (parts.Length == 2 && int.TryParse(parts[0], out int minutes) && int.TryParse(parts[1], out int seconds))
                                {
                                    track.DurationTimeSpan = new TimeSpan(0, minutes, seconds);
                                }
                            }
                            catch
                            {
                                // If parsing fails, try to get duration from file
                                try
                                {
                                    var atlTrack = new ATL.Track(track.FilePath);
                                    if (atlTrack.Duration > 0)
                                    {
                                        track.DurationTimeSpan = TimeSpan.FromSeconds(atlTrack.Duration);
                                    }
                                }
                                catch
                                {
                                    // If all else fails, leave it as zero
                                }
                            }
                        }
                        
                        // Calculate bitrate from file size and duration if still not set
                        if (string.IsNullOrEmpty(track.Bitrate) && track.FileSize > 0 && track.DurationTimeSpan.TotalSeconds > 0)
                        {
                            var bitrateKbps = (int)((track.FileSize * 8) / (track.DurationTimeSpan.TotalSeconds * 1000));
                            if (bitrateKbps > 0)
                            {
                                track.Bitrate = $"{bitrateKbps} kbps";
                            }
                        }

                        allTracks.Add(track);
                        filteredTracks.Add(track);
                    }
                }

                // Backfill thumbnail cache in background for tracks that don't have one yet.
                // This avoids blocking startup; the cache paths are written back on completion.
                var tracksNeedingThumbnails = allTracks
                    .Where(t => string.IsNullOrEmpty(t.ThumbnailCachePath) || !File.Exists(t.ThumbnailCachePath))
                    .ToList();

                if (tracksNeedingThumbnails.Count > 0)
                {
                    _ = Task.Run(async () =>
                    {
                        foreach (var t in tracksNeedingThumbnails)
                        {
                            t.ThumbnailCachePath = AlbumArtCacheManager.GenerateAndCache(t);
                        }
                        await Dispatcher.InvokeAsync(async () => await UpdateLibraryCacheAsync());
                    });
                }

                Console.WriteLine($"After loading from cache - allTracks: {allTracks.Count}, filteredTracks: {filteredTracks.Count}");
                UpdateShuffledTracks();

                RefreshVisibleViews();

                await Dispatcher.InvokeAsync(() =>
                {
                    UpdateStatusBar();
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading from cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Restores playlists from saved data
        /// </summary>
        private void RestorePlaylists(LibraryManager.PlaylistsCache playlistsCache)
        {
            if (playlistsCache.Playlists != null)
            {
                foreach (var playlist in playlistsCache.Playlists)
                {
                    playlist.ReconstructTracks(allTracks);
                    playlists.Add(playlist);
                }
            }
        }

        private async void PlaylistsViewControl_CreatePlaylistRequested(object? sender, EventArgs e)
        {
            var name = TextInputDialog.Show(this, "Create Playlist", "Enter playlist name:", "New Playlist");
            if (string.IsNullOrWhiteSpace(name))
                return;

            var playlist = new Playlist(name);
            libraryManager.AddPlaylist(playlists, playlist);
            await libraryManager.SavePlaylistsFromCollectionAsync(playlists);
            UpdateUI();
        }

        private async void PlaylistsViewControl_DeletePlaylistRequested(object? sender, Playlist e)
        {
            if (e == null)
                return;

            var result = MessageDialog.Show(this, "Delete Playlist", $"Delete playlist \"{e.Name}\"?", MessageDialog.Buttons.YesNo);
            if (result != true)
                return;

            PinnedPlaylists.Remove(e);
            OnPropertyChanged(nameof(HasPinnedPlaylists));
            libraryManager.DeletePlaylist(playlists, e);
            await libraryManager.SavePlaylistsFromCollectionAsync(playlists);
            UpdateUI();
        }

        private async void PlaylistsViewControl_PlaylistPinnedChanged(object? sender, (Playlist playlist, bool isPinned) e)
        {
            if (e.isPinned)
            {
                if (!PinnedPlaylists.Contains(e.playlist))
                    PinnedPlaylists.Add(e.playlist);
            }
            else
            {
                PinnedPlaylists.Remove(e.playlist);
            }
            OnPropertyChanged(nameof(HasPinnedPlaylists));
            await libraryManager.SavePlaylistsFromCollectionAsync(playlists);
        }

        private async void PlaylistsViewControl_ImportPlaylistRequested(object? sender, EventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "M3U Playlists|*.m3u;*.m3u8|All Files|*.*",
                Title = "Import Playlist"
            };

            var result = dialog.ShowDialog(this);
            if (result != true)
                return;

            try
            {
                var imported = libraryManager.ImportPlaylistFromM3u(dialog.FileName, System.IO.Path.GetFileNameWithoutExtension(dialog.FileName), allTracks);
                libraryManager.AddPlaylist(playlists, imported);
                await libraryManager.SavePlaylistsFromCollectionAsync(playlists);
                UpdateUI();
            }
            catch (Exception ex)
            {
                MessageDialog.Show(this, "Error", $"Failed to import playlist: {ex.Message}", MessageDialog.Buttons.Ok);
            }
        }

        private void PlaylistsViewControl_ExportPlaylistRequested(object? sender, Playlist e)
        {
            if (e == null)
                return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "M3U Playlists|*.m3u;*.m3u8|All Files|*.*",
                Title = "Export Playlist",
                FileName = $"{e.Name}.m3u"
            };

            var result = dialog.ShowDialog(this);
            if (result != true)
                return;

            try
            {
                libraryManager.ExportPlaylistToM3u(e, dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageDialog.Show(this, "Error", $"Failed to export playlist: {ex.Message}", MessageDialog.Buttons.Ok);
            }
        }

        /// <summary>
        /// Restores recently played tracks
        /// </summary>
        private void RestoreRecentlyPlayed(LibraryManager.RecentlyPlayedCache recentlyPlayedCache)
        {
            if (recentlyPlayedCache.RecentlyPlayed != null)
            {
                foreach (var item in recentlyPlayedCache.RecentlyPlayed.OrderByDescending(x => x.LastPlayed).Take(20))
                {
                    var track = allTracks.FirstOrDefault(t => t.FilePath == item.FilePath);
                    if (track != null)
                    {
                        recentlyPlayed.Add(track);
                    }
                }
            }
        }

        /// <summary>
        /// Updates the UI after loading data
        /// </summary>
        private void UpdateUI()
        {
            RefreshAllViewDataSources();
            RefreshVisibleViews();
        }

        /// <summary>
        /// Updates the status bar with library statistics
        /// </summary>
        private void UpdateStatusBar()
        {
            try
            {
                if (statusBarText == null || allTracks == null || allTracks.Count == 0)
                {
                    if (statusBarText != null)
                    {
                        statusBarText.Text = "0 songs, 0 albums, 0.0 days, 0.00 GB";
                    }
                    return;
                }

                var totalTracks = allTracks.Count;

                var uniqueAlbums = allTracks
                    .Where(t => !string.IsNullOrWhiteSpace(t.Album) && t.Album != "Unknown Album")
                    .Select(t => new { t.Album, t.Artist })
                    .Distinct()
                    .Count();

                var totalDuration = allTracks
                    .Where(t => t.DurationTimeSpan != TimeSpan.Zero)
                    .Sum(t => t.DurationTimeSpan.TotalSeconds);
                var totalDays = totalDuration / (24.0 * 3600.0);

                // Calculate total file size in GB (use cached FileSize, avoid File.Exists checks)
                long totalBytes = 0;
                foreach (var track in allTracks)
                {
                    if (track.FileSize > 0)
                    {
                        totalBytes += track.FileSize;
                    }
                    // Only check file system if FileSize is not cached (should be rare after initial load)
                    else if (!string.IsNullOrEmpty(track.FilePath))
                    {
                        try
                        {
                            var fileInfo = new FileInfo(track.FilePath);
                            if (fileInfo.Exists)
                            {
                                totalBytes += fileInfo.Length;
                                track.FileSize = fileInfo.Length;
                            }
                        }
                        catch
                        {
                        }
                    }
                }
                var totalGB = totalBytes / (1024.0 * 1024.0 * 1024.0);

                statusBarText.Text = $"{totalTracks} songs, {uniqueAlbums} albums, {totalDays:F1} days, {totalGB:F2} GB";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating status bar: {ex.Message}");
                if (statusBarText != null)
                {
                    statusBarText.Text = "Error calculating statistics";
                }
            }
        }

        /// <summary>
        /// Sets up data bindings and event handlers for UI controls
        /// </summary>
        private void SetupEventHandlers()
        {
            RefreshAllViewDataSources();

            titleBarPlayer.PlayPauseRequested += TitleBarPlayer_PlayPauseRequested;
            titleBarPlayer.PreviousTrackRequested += TitleBarPlayer_PreviousTrackRequested;
            titleBarPlayer.NextTrackRequested += TitleBarPlayer_NextTrackRequested;
            titleBarPlayer.WindowMinimizeRequested += TitleBarPlayer_WindowMinimizeRequested;
            titleBarPlayer.WindowMaximizeRequested += TitleBarPlayer_WindowMaximizeRequested;
            titleBarPlayer.WindowCloseRequested += TitleBarPlayer_WindowCloseRequested;

            titleBarPlayer.ShuffleStateChanged += TitleBarPlayer_ShuffleStateChanged;

            titleBarPlayer.ArtistNavigationRequested += TitleBarPlayer_ArtistNavigationRequested;
            titleBarPlayer.AlbumNavigationRequested += TitleBarPlayer_AlbumNavigationRequested;

            titleBarPlayer.SearchTextChanged += TitleBarPlayer_SearchTextChanged;
            if (searchPopupView != null)
            {
                searchPopupView.SongSelected += SearchPopupView_SongSelected;
                searchPopupView.ArtistSelected += SearchPopupView_ArtistSelected;
                searchPopupView.AlbumSelected += SearchPopupView_AlbumSelected;
                searchPopupView.PlayNextRequested += OnPlayNextRequested;
                searchPopupView.AddToQueueRequested += OnAddToQueueRequested;
                searchPopupView.AddTrackToPlaylistRequested += OnAddTrackToPlaylistRequested;
                searchPopupView.CreateNewPlaylistWithTrackRequested += OnCreateNewPlaylistWithTrackRequested;
                searchPopupView.InfoRequested += OnInfoRequested;
                searchPopupView.ShowInArtistsRequested += OnShowInArtistsRequested;
                searchPopupView.ShowInSongsRequested += OnShowInSongsRequested;
                searchPopupView.ShowInAlbumsRequested += OnShowInAlbumsRequested;
                searchPopupView.ShowInQueueRequested += OnShowInQueueRequested;
                searchPopupView.ShowInExplorerRequested += OnShowInExplorerRequested;
                searchPopupView.RemoveFromLibraryRequested += OnRemoveFromLibraryRequested;
                searchPopupView.DeleteRequested += OnDeleteRequested;
            }

            this.SizeChanged += MainWindow_SizeChanged;
        }

        private static bool IsValidTrackWithPath(Song? track)
        {
            return track != null && !string.IsNullOrWhiteSpace(track.FilePath);
        }

        private void RebuildAllViewColumns()
        {
            songsView?.RebuildColumns();
            queueViewControl?.RebuildColumns();
            recentlyPlayedViewControl?.RebuildColumns();
            artistsViewControl?.RebuildColumns();
            genresViewControl?.RebuildColumns();
            albumsViewControl?.RebuildColumns();
        }

        private void RefreshAllViewDataSources()
        {
            if (songsView != null) songsView.ItemsSource = allTracks;
            if (playlistsViewControl != null) playlistsViewControl.Playlists = playlists;
            if (recentlyPlayedViewControl != null) recentlyPlayedViewControl.ItemsSource = recentlyPlayed;
            if (artistsViewControl != null) artistsViewControl.ItemsSource = allTracks;
            if (genresViewControl != null) genresViewControl.ItemsSource = allTracks;
            if (albumsViewControl != null) albumsViewControl.ItemsSource = allTracks;
        }

        private void RefreshVisibleViews()
        {
            var current = contentHost?.Content;
            switch (current)
            {
                case object _ when ReferenceEquals(current, queueViewControl):
                    UpdateQueueView();
                    break;
                case object _ when ReferenceEquals(current, playlistsViewControl):
                    UpdatePlaylistsView();
                    break;
                default:
                    break;
            }
        }

        private void RefreshAfterMetadataEdit(Song updatedTrack)
        {
            songsView?.RefreshTrackListBindings();
            queueViewControl?.RefreshTrackListBindings();
            recentlyPlayedViewControl?.RefreshTrackListBindings();
            artistsViewControl?.RefreshTrackListBindings();
            genresViewControl?.RefreshTrackListBindings();
            albumsViewControl?.RefreshAlbumGridFromLibrary();
            playlistsViewControl?.RefreshTrackListBindings();

            if (currentTrack != null && updatedTrack != null &&
                string.Equals(currentTrack.FilePath, updatedTrack.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                var albumArt = AlbumArtLoader.LoadAlbumArt(currentTrack);
                titleBarPlayer.SetTrackInfo(currentTrack.Title, currentTrack.Artist, currentTrack.Album, albumArt);
            }

            UpdateStatusBar();
        }

        private void RemoveTrackFromCollections(Song track, bool includeShuffled)
        {
            allTracks.Remove(track);
            filteredTracks.Remove(track);
            if (includeShuffled)
            {
                shuffledTracks.Remove(track);
            }
            recentlyPlayed.Remove(track);
        }

        private static void LogDebug(string message)
        {
            Debug.WriteLine(message);
        }

        private void TitleBarPlayer_SearchTextChanged(object? sender, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                searchPopup.IsOpen = false;
                return;
            }
            var results = SearchHelper.Run(query, allTracks);
            if (searchPopupView != null)
                searchPopupView.Results = results;
            if (searchPopup.PlacementTarget == null && titleBarPlayer.SearchBarBorder != null)
                searchPopup.PlacementTarget = titleBarPlayer.SearchBarBorder;
            searchPopup.IsOpen = true;
            searchPopupView?.RefreshHeightForSearch();
        }

        private void SearchPopupView_SongSelected(object? sender, Song song)
        {
            searchPopup.IsOpen = false;
            PlayTrack(song);
        }

        private void SearchPopupView_ArtistSelected(object? sender, ArtistSearchItem artist)
        {
            searchPopup.IsOpen = false;
            ShowArtistsView();
            artistsViewControl?.SelectArtist(artist.Name);
        }

        private void SearchPopupView_AlbumSelected(object? sender, AlbumSearchItem album)
        {
            searchPopup.IsOpen = false;
            ShowAlbumsView();
            if (albumsViewControl != null && album.Songs.Count > 0)
                albumsViewControl.ItemsSource = album.Songs;
        }

        private void TitleBarPlayer_ArtistNavigationRequested(object? sender, string artistName)
        {
            ShowArtistsView();
            artistsViewControl?.SelectArtist(artistName);
        }

        private void TitleBarPlayer_AlbumNavigationRequested(object? sender, string albumName)
        {
            ShowAlbumsView();
            albumsViewControl?.ScrollToAlbum(albumName);
        }

        private void AlbumsView_ArtistNavigationRequested(object? sender, string artistName)
        {
            ShowArtistsView();
            artistsViewControl?.SelectArtist(artistName);
        }

        private void AlbumsView_GenreNavigationRequested(object? sender, string genreName)
        {
            ShowGenresView();
            genresViewControl?.SelectGenre(genreName);
        }

        #region Shuffle Management

        /// <summary>
        /// Regenerates the shuffled tracks collection and updates current track index
        /// This should only be called when shuffle is first enabled or when explicitly requested
        /// </summary>
        private void RegenerateShuffledTracks()
        {
            try
            {
                Console.WriteLine($"RegenerateShuffledTracks called - filteredTracks.Count: {filteredTracks.Count}");

                if (filteredTracks == null || filteredTracks.Count == 0)
                {
                    Console.WriteLine("No filtered tracks to shuffle");
                    shuffledTracks.Clear();
                    currentShuffledIndex = -1;
                    return;
                }

                shuffledTracks.Clear();

                Console.WriteLine($"Processing {filteredTracks.Count} tracks from filteredTracks");
                foreach (var track in filteredTracks)
                {
                    if (track != null && !string.IsNullOrEmpty(track.FilePath))
                    {
                        shuffledTracks.Add(track);
                    }
                    else
                    {
                        Console.WriteLine($"Skipping null or invalid track in filteredTracks");
                    }
                }

                Console.WriteLine($"Added {shuffledTracks.Count} valid tracks to shuffled queue");
                Console.WriteLine($"Shuffle queue now contains: {string.Join(", ", shuffledTracks.Take(5).Select(t => t.Title))}... (and {shuffledTracks.Count - 5} more)");

                if (shuffledTracks.Count > 1)
                {
                    var random = new Random();
                    for (int i = shuffledTracks.Count - 1; i > 0; i--)
                    {
                        int j = random.Next(i + 1);
                        var temp = shuffledTracks[i];
                        shuffledTracks[i] = shuffledTracks[j];
                        shuffledTracks[j] = temp;
                    }
                    Console.WriteLine("Shuffled tracks using Fisher-Yates algorithm");
                }
                else
                {
                    Console.WriteLine("Not enough tracks to shuffle (need at least 2)");
                }

                if (currentTrack != null)
                {
                    int trackIndex = shuffledTracks.IndexOf(currentTrack);
                    if (trackIndex > 0)
                    {
                        var trackToMove = shuffledTracks[trackIndex];
                        shuffledTracks.RemoveAt(trackIndex);
                        shuffledTracks.Insert(0, trackToMove);
                        Console.WriteLine($"Moved current track '{currentTrack.Title}' to beginning of shuffled queue");
                    }
                    else if (trackIndex == 0)
                    {
                        Console.WriteLine($"Current track '{currentTrack.Title}' is already at beginning of shuffled queue");
                    }
                    else
                    {
                        Console.WriteLine($"Current track '{currentTrack.Title}' not found in shuffled list, this shouldn't happen");
                    }

                    currentShuffledIndex = 0;
                }
                else
                {
                    currentShuffledIndex = -1;
                }

                Console.WriteLine($"Shuffled tracks generated - shuffledTracks.Count: {shuffledTracks.Count}, currentShuffledIndex: {currentShuffledIndex}");
                Console.WriteLine($"Shuffle queue order: {string.Join(" -> ", shuffledTracks.Take(5).Select(t => t.Title))}...");

                if (contentHost?.Content == queueViewControl)
                {
                    UpdateQueueView();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RegenerateShuffledTracks: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");

                try
                {
                    shuffledTracks.Clear();
                    currentShuffledIndex = -1;
                }
                catch (Exception clearEx)
                {
                    Console.WriteLine($"Error clearing shuffled tracks: {clearEx.Message}");
                }
            }
        }

        /// <summary>
        /// Ensures the shuffled tracks collection is properly initialized when shuffle is enabled
        /// This method maintains the existing shuffled order if possible, only regenerating when necessary
        /// </summary>
        private void EnsureShuffledTracksInitialized()
        {
            try
            {
                if (!titleBarPlayer.IsShuffleEnabled)
                {
                    return;
                }

                Console.WriteLine($"EnsureShuffledTracksInitialized called - shuffledTracks.Count: {shuffledTracks.Count}, currentShuffledIndex: {currentShuffledIndex}");

                if (shuffledTracks.Count == 0 || shuffledTracks.Count != filteredTracks.Count)
                {
                    Console.WriteLine("Shuffled tracks count mismatch or empty, regenerating");
                    RegenerateShuffledTracks();
                    return;
                }

                if (currentTrack != null && shuffledTracks.IndexOf(currentTrack) == -1)
                {
                    Console.WriteLine("Current track not found in shuffled tracks, regenerating");
                    RegenerateShuffledTracks();
                    return;
                }

                if (currentTrack != null && currentShuffledIndex == -1)
                {
                    currentShuffledIndex = shuffledTracks.IndexOf(currentTrack);
                    if (currentShuffledIndex == -1)
                    {
                        Console.WriteLine("Could not find current track in shuffled tracks, regenerating");
                        RegenerateShuffledTracks();
                    }
                    else
                    {
                        Console.WriteLine($"Found current track in shuffled tracks at index: {currentShuffledIndex}");
                    }
                }

                Console.WriteLine($"Shuffled tracks properly initialized - shuffledTracks.Count: {shuffledTracks.Count}, currentShuffledIndex: {currentShuffledIndex}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in EnsureShuffledTracksInitialized: {ex.Message}");
                RegenerateShuffledTracks();
            }
        }

        /// <summary>
        /// Updates the shuffled tracks collection when filtered tracks change
        /// This should only be called when the library content changes, not during normal playback
        /// </summary>
        private void UpdateShuffledTracks()
        {
            Console.WriteLine($"UpdateShuffledTracks called - shuffle enabled: {titleBarPlayer.IsShuffleEnabled}, filteredTracks: {filteredTracks.Count}, shuffledTracks: {shuffledTracks.Count}");

            if (titleBarPlayer.IsShuffleEnabled)
            {
                // Only regenerate if the library content has actually changed
                if (shuffledTracks.Count != filteredTracks.Count)
                {
                    Console.WriteLine("Library content changed, regenerating shuffled tracks");
                    RegenerateShuffledTracks();
                }
                else
                {
                    Console.WriteLine("Library content unchanged, maintaining existing shuffled order");
                    EnsureShuffledTracksInitialized();
                }
            }
        }

        private void UpdateShuffleIndicesAfterTrackChange(Song track)
        {
            if (!titleBarPlayer.IsShuffleEnabled || HasContextualPlaybackQueue())
                return;

            if (!isManualNavigation)
            {
                Console.WriteLine("Shuffle enabled and not manual navigation - regenerating shuffled queue");
                RegenerateShuffledTracks();
            }
            else
            {
                Console.WriteLine("Shuffle enabled but manual navigation - maintaining existing shuffled queue");
            }

            currentShuffledIndex = shuffledTracks.IndexOf(track);
        }

        /// <summary>
        /// Gets the current play queue (either filtered or shuffled based on shuffle state)
        /// </summary>
        private ObservableCollection<Song> GetCurrentPlayQueue()
        {
            try
            {
                if (contextualPlaybackQueue != null && contextualPlaybackQueue.Count > 0)
                {
                    Console.WriteLine($"GetCurrentPlayQueue - using contextual queue with {contextualPlaybackQueue.Count} tracks");
                    return contextualPlaybackQueue;
                }

                var queue = titleBarPlayer.IsShuffleEnabled ? shuffledTracks : filteredTracks;

                if (queue == null)
                {
                    Console.WriteLine("GetCurrentPlayQueue: queue is null, falling back to filteredTracks");
                    queue = filteredTracks;
                }

                Console.WriteLine($"GetCurrentPlayQueue - shuffle enabled: {titleBarPlayer.IsShuffleEnabled}, queue count: {queue?.Count ?? 0}, filteredTracks: {filteredTracks.Count}, shuffledTracks: {shuffledTracks.Count}");

                if (titleBarPlayer.IsShuffleEnabled && shuffledTracks.Count > 0)
                {
                    Console.WriteLine($"Shuffle queue sample: {string.Join(", ", shuffledTracks.Take(3).Select(t => t.Title))}...");
                }

                return queue ?? new ObservableCollection<Song>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetCurrentPlayQueue: {ex.Message}");
                return filteredTracks ?? new ObservableCollection<Song>();
            }
        }

        /// <summary>
        /// Gets the current track index in the current play queue
        /// </summary>
        private int GetCurrentTrackIndex()
        {
            try
            {
                if (contextualPlaybackQueue != null && contextualPlaybackQueue.Count > 0)
                {
                    Console.WriteLine($"GetCurrentTrackIndex - using contextual index: {contextualPlaybackIndex}");
                    return contextualPlaybackIndex;
                }

                var index = titleBarPlayer.IsShuffleEnabled ? currentShuffledIndex : currentTrackIndex;
                Console.WriteLine($"GetCurrentTrackIndex - shuffle enabled: {titleBarPlayer.IsShuffleEnabled}, index: {index}");
                return index;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetCurrentTrackIndex: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Sets the current track index in the current play queue
        /// </summary>
        private void SetCurrentTrackIndex(int index)
        {
            if (contextualPlaybackQueue != null && contextualPlaybackQueue.Count > 0)
            {
                contextualPlaybackIndex = index;
                return;
            }

            if (titleBarPlayer.IsShuffleEnabled)
            {
                currentShuffledIndex = index;
            }
            else
            {
                currentTrackIndex = index;
            }
        }

        /// <summary>
        /// Safely gets a track from the current play queue at the specified index
        /// </summary>
        private Song? GetTrackFromCurrentQueue(int index)
        {
            try
            {
                var queue = GetCurrentPlayQueue();
                if (queue != null && index >= 0 && index < queue.Count)
                {
                    var track = queue[index];
                    if (track != null && !string.IsNullOrEmpty(track.FilePath))
                    {
                        return track;
                    }
                    else
                    {
                        Console.WriteLine($"Track at index {index} is null or has invalid file path");
                    }
                }
                else
                {
                    Console.WriteLine($"Invalid index {index} or queue is null/empty (count: {queue?.Count ?? 0})");
                }
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting track from queue at index {index}: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Title Bar Player Control Event Handlers

        private enum PreviousTrackSeekBehavior
        {
            RestartCurrent,
            GoToPrevious,
            RestartCurrentEdge,
        }

        private static PreviousTrackSeekBehavior GetPreviousTrackSeekBehavior(double elapsedSeconds, int currentIndex)
        {
            if (elapsedSeconds >= UILayoutConstants.PreviousTrackRestartThresholdSeconds)
                return PreviousTrackSeekBehavior.RestartCurrent;
            if (elapsedSeconds <= UILayoutConstants.PreviousTrackEdgeThresholdSeconds && currentIndex > 0)
                return PreviousTrackSeekBehavior.GoToPrevious;
            return PreviousTrackSeekBehavior.RestartCurrentEdge;
        }

        private void RestartCurrentTrackFromPreviousButton(bool resumeIfWasPlaying)
        {
            if (currentTrack == null)
                return;
            LoadTrackWithoutPlayback(currentTrack);
            if (resumeIfWasPlaying)
                ResumePlayback();
        }

        private void TitleBarPlayer_PlayPauseRequested(object? sender, EventArgs e)
        {
            if (currentTrack == null)
            {
                if (contentHost?.Content == songsView && songsView?.SelectedTrack is Song selectedSong)
                {
                    PlayTrack(selectedSong);
                }
                else if (filteredTracks.Count > 0)
                {
                    PlayTrack(filteredTracks[0]);
                }
                return;
            }

            if (titleBarPlayer.IsPlaying)
            {
                PausePlayback();
            }
            else
            {
                ResumePlayback();
            }
        }

        private void TitleBarPlayer_PreviousTrackRequested(object? sender, EventArgs e)
        {
            var currentQueue = GetCurrentPlayQueue();
            var currentIndex = GetCurrentTrackIndex();

            Console.WriteLine($"Previous track requested - currentIndex: {currentIndex}, queue count: {currentQueue.Count}");

            isManualNavigation = true;

            var currentPosition = titleBarPlayer.CurrentPosition;
            Console.WriteLine($"Current playback position: {currentPosition.TotalSeconds:F1} seconds");

            bool wasPlaying = titleBarPlayer.IsPlaying;
            var behavior = GetPreviousTrackSeekBehavior(currentPosition.TotalSeconds, currentIndex);

            switch (behavior)
            {
                case PreviousTrackSeekBehavior.RestartCurrent:
                    Console.WriteLine("Restarting current track (3+ seconds elapsed)");
                    RestartCurrentTrackFromPreviousButton(wasPlaying);
                    break;

                case PreviousTrackSeekBehavior.GoToPrevious:
                    Console.WriteLine("Going to previous track (2 seconds or less elapsed)");
                    var previousTrack = GetTrackFromCurrentQueue(currentIndex - 1);
                    if (previousTrack != null)
                    {
                        LoadTrackWithoutPlayback(previousTrack);
                        if (wasPlaying)
                            ResumePlayback();
                    }
                    else
                    {
                        Console.WriteLine("Previous track is null or invalid, cannot play");
                        if (contentHost?.Content == queueViewControl)
                            UpdateQueueView();
                    }
                    break;

                case PreviousTrackSeekBehavior.RestartCurrentEdge:
                    Console.WriteLine("Restarting current track (2–3s band, or ≤2s at start of queue)");
                    RestartCurrentTrackFromPreviousButton(wasPlaying);
                    break;
            }

            Task.Delay(UILayoutConstants.ManualNavigationResetDelayMs).ContinueWith(_ => isManualNavigation = false);
        }

        private void ResetPlaybackToIdleAndRefreshQueue()
        {
            CleanupAudioObjects();
        }

        private void TitleBarPlayer_NextTrackRequested(object? sender, EventArgs e)
        {
            var currentQueue = GetCurrentPlayQueue();
            var currentIndex = GetCurrentTrackIndex();

            Console.WriteLine($"Next track requested - currentIndex: {currentIndex}, queue count: {currentQueue.Count}");

            isManualNavigation = true;

            if (currentIndex < currentQueue.Count - 1)
            {
                var nextTrack = GetTrackFromCurrentQueue(currentIndex + 1);
                if (nextTrack != null)
                {
                    bool wasPlaying = titleBarPlayer.IsPlaying;

                    LoadTrackWithoutPlayback(nextTrack);
                    if (wasPlaying)
                    {
                        ResumePlayback();
                    }
                }
                else
                {
                    Console.WriteLine("Next track is null or has invalid file path, stopping playback");
                    ResetPlaybackToIdleAndRefreshQueue();
                }
            }
            else
            {
                Console.WriteLine("Reached end of queue, stopping playback and resetting to idle state");
                ResetPlaybackToIdleAndRefreshQueue();
                Console.WriteLine("Playback stopped - app reset to idle state");
            }

            Task.Delay(UILayoutConstants.ManualNavigationResetDelayMs).ContinueWith(_ => isManualNavigation = false);
        }

        private void TitleBarPlayer_WindowMinimizeRequested(object? sender, EventArgs e)
        {
            windowManager.MinimizeWindow();
        }

        private void TitleBarPlayer_WindowMaximizeRequested(object? sender, EventArgs e)
        {
            windowManager.ToggleMaximize();
        }





        /// <summary>
        /// Updates the window state tracking when the window state changes externally
        /// </summary>
        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            windowManager.OnStateChanged();
        }

        /// <summary>
        /// Handles window activation to restore custom window style after minimize/restore operations
        /// </summary>
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);

            if (WindowStyle != WindowStyle.None)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (DispatcherOperationCallback)delegate (object unused)
                {
                    WindowStyle = WindowStyle.None;

                    // After restoring the window style, check if the window is visually maximized
                    // This helps fix the issue where minimize/restore of maximized windows
                    // doesn't properly update the maximize button icon
                    windowManager.CheckIfWindowIsVisuallyMaximized();

                    return null;
                }, null);
            }
        }

        /// <summary>
        /// Handles window location and size changes to update state tracking
        /// </summary>
        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            windowManager.OnLocationChanged();
        }

        /// <summary>
        /// Handles window size changed events
        /// </summary>
        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            windowManager.OnSizeChanged();
        }


        /// <summary>
        /// Sets up tracking for sidebar width changes
        /// </summary>
        private void SetupSidebarWidthTracking()
        {
            sidebarWidthSaveTimer = new DispatcherTimer(
                UILayoutConstants.SidebarWidthSaveDelay, 
                DispatcherPriority.Background, 
                SidebarWidthSaveTimer_Tick, 
                Dispatcher.CurrentDispatcher);
        }

        /// <summary>
        /// Handles GridSplitter drag completed event
        /// </summary>
        private void GridSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            MarkSidebarWidthDirty();
        }

        /// <summary>
        /// Marks the sidebar width as dirty and starts the save timer
        /// </summary>
        private void MarkSidebarWidthDirty()
        {
            isSidebarWidthDirty = true;
            
            if (sidebarWidthSaveTimer != null && !sidebarWidthSaveTimer.IsEnabled)
            {
                sidebarWidthSaveTimer.Start();
            }
        }

        /// <summary>
        /// Timer callback to save the sidebar width
        /// </summary>
        private async void SidebarWidthSaveTimer_Tick(object? sender, EventArgs e)
        {
            if (isSidebarWidthDirty)
            {
                isSidebarWidthDirty = false;
                sidebarWidthSaveTimer?.Stop();
                
                try
                {
                    if (appSettings.WindowState != null)
                    {
                        appSettings.WindowState.SidebarWidth = sidebarColumn.ActualWidth;
                        await settingsManager.SaveSettingsAsync(appSettings);
                        System.Diagnostics.Debug.WriteLine($"MainWindow: Sidebar width saved: {sidebarColumn.ActualWidth}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving sidebar width: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Handles window state changes from the WindowManager
        /// </summary>
        private async void WindowManager_WindowStateChanged(object? sender, EventArgs e)
        {
            try
            {
                appSettings.WindowState = windowManager.GetCurrentWindowState();

                if (appSettings.WindowState != null)
                {
                    appSettings.WindowState.SidebarWidth = sidebarColumn.ActualWidth;

                }

                await settingsManager.SaveSettingsAsync(appSettings);

                System.Diagnostics.Debug.WriteLine("MainWindow: Window state saved successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MainWindow: Error saving window state: {ex.Message}");
            }
        }

        private void TitleBarPlayer_WindowCloseRequested(object? sender, EventArgs e)
        {
            windowManager.CloseWindow();
        }

        private void TitleBarPlayer_ShuffleStateChanged(object? sender, bool isShuffleEnabled)
        {
            Console.WriteLine($"Shuffle state changed to: {isShuffleEnabled}");

            if (isShuffleEnabled)
            {
                Console.WriteLine("Shuffle enabled - regenerating shuffled tracks");
                RegenerateShuffledTracks();
            }
            else
            {
                // Shuffle was disabled - we need to find the current track in the original filtered list
                // and update the currentTrackIndex to maintain the current position
                Console.WriteLine("Shuffle disabled - updating current track index in filtered list");
                if (currentTrack != null)
                {
                    currentTrackIndex = filteredTracks.IndexOf(currentTrack);
                    if (currentTrackIndex == -1)
                    {
                        currentTrackIndex = 0;
                    }
                    Console.WriteLine($"Current track index updated to: {currentTrackIndex}");
                }
            }

            if (contentHost?.Content == queueViewControl)
            {
                UpdateQueueView();
            }
        }

        #endregion

        #region Navigation Events
        #endregion

        #region View Management
        #endregion

        #region Queue Management

        /// <summary>
        /// Inserts the given track to play immediately after the current track (play next).
        /// </summary>
        private void OnPlayNextRequested(object? sender, Song track)
        {
            if (!IsValidTrackWithPath(track))
                return;

            int insertAt = GetCurrentTrackIndex() + 1;
            if (insertAt < 0)
                insertAt = 0;

            if (insertAt <= filteredTracks.Count)
                filteredTracks.Insert(insertAt, track);
            else
                filteredTracks.Add(track);

            int shuffleInsertAt = (titleBarPlayer.IsShuffleEnabled ? currentShuffledIndex : currentTrackIndex) + 1;
            if (shuffleInsertAt < 0)
                shuffleInsertAt = 0;
            if (shuffleInsertAt <= shuffledTracks.Count)
                shuffledTracks.Insert(shuffleInsertAt, track);
            else
                shuffledTracks.Add(track);

            RefreshVisibleViews();
        }

        /// <summary>
        /// Appends the given track to the end of the current queue.
        /// </summary>
        private void OnAddToQueueRequested(object? sender, Song track)
        {
            if (!IsValidTrackWithPath(track))
                return;

            filteredTracks.Add(track);
            shuffledTracks.Add(track);

            RefreshVisibleViews();
        }

        /// <summary>
        /// Returns true when the given track exists in the effective queue view
        /// (current song + upcoming songs). Idle state returns false.
        /// </summary>
        public bool IsTrackInQueue(Song? track)
        {
            if (track == null)
                return false;

            var queue = BuildQueueView();
            if (queue == null || queue.Count == 0)
                return false;

            if (!string.IsNullOrWhiteSpace(track.FilePath))
            {
                for (int i = 0; i < queue.Count; i++)
                {
                    var queued = queue[i];
                    if (!string.IsNullOrWhiteSpace(queued.FilePath) &&
                        string.Equals(queued.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            for (int i = 0; i < queue.Count; i++)
            {
                var queued = queue[i];
                if (string.Equals(queued.Title, track.Title, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(queued.Artist, track.Artist, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(queued.Album, track.Album, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private async void OnAddTrackToPlaylistRequested(object? sender, (Song track, Playlist playlist) args)
        {
            if (args.track == null || args.playlist == null)
                return;
            args.playlist.AddTrack(args.track);
            await libraryManager.SavePlaylistsFromCollectionAsync(playlists);
        }

        private async void OnCreateNewPlaylistWithTrackRequested(object? sender, Song track)
        {
            if (track == null)
                return;
            var name = TextInputDialog.Show(this, "New Playlist", "Playlist name:", "New Playlist");
            if (string.IsNullOrWhiteSpace(name))
                return;
            var playlist = new Playlist(name.Trim());
            playlist.AddTrack(track);
            libraryManager.AddPlaylist(playlists, playlist);
            await libraryManager.SavePlaylistsFromCollectionAsync(playlists);
            UpdateUI();
        }

        private async void OnRemoveFromPlaylistRequested(object? sender, (Song track, Playlist playlist) args)
        {
            if (args.track == null || args.playlist == null)
                return;
            args.playlist.RemoveTrack(args.track);
            await libraryManager.SavePlaylistsFromCollectionAsync(playlists);
        }

        /// <summary>
        /// Removes the track from the MusicApp library (in-memory and persisted). Does not delete the file.
        /// </summary>
        private async void OnRemoveFromLibraryRequested(object? sender, Song track)
        {
            if (!IsValidTrackWithPath(track))
                return;
            var result = MessageDialog.Show(this, "Remove from Library", $"Remove \"{track.Title}\" from the library? The file will stay on your computer.", MessageDialog.Buttons.YesNo);
            if (result != true)
                return;
            await RemoveTrackFromLibraryAsync(track);
        }

        /// <summary>
        /// Moves the track's file to the recycle bin, then removes it from the library.
        /// </summary>
        private async void OnDeleteRequested(object? sender, Song track)
        {
            if (!IsValidTrackWithPath(track))
                return;
            var result = MessageDialog.Show(this, "Delete", $"Move \"{track.Title}\" to the recycle bin?", MessageDialog.Buttons.YesNo);
            if (result != true)
                return;
            if (!File.Exists(track.FilePath))
            {
                // File already gone; just remove from library
                await RemoveTrackFromLibraryAsync(track);
                return;
            }
            if (!RecycleBinHelper.MoveFileToRecycleBin(track.FilePath))
            {
                MessageDialog.Show(this, "Error", $"Could not move file to recycle bin: {track.FilePath}", MessageDialog.Buttons.Ok);
                return;
            }
            await RemoveTrackFromLibraryAsync(track);
        }

        /// <summary>
        /// Removes a track from all in-memory collections, playlists, and persisted caches. Stops playback if this track is current.
        /// </summary>
        private async Task RemoveTrackFromLibraryAsync(Song track)
        {
            if (track == null)
                return;

            var path = track.FilePath;

            if (currentTrack != null && string.Equals(currentTrack.FilePath, path, StringComparison.OrdinalIgnoreCase))
            {
                CleanupAudioObjects();
                currentTrack = null;
                currentTrackIndex = -1;
                currentShuffledIndex = -1;
                ClearContextualPlaybackQueue();
                titleBarPlayer.SetTrackInfo("No track selected", "", "");
            }

            RemoveTrackFromCollections(track, includeShuffled: true);

            foreach (var playlist in playlists)
            {
                var toRemove = playlist.Tracks.FirstOrDefault(t => string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));
                if (toRemove != null)
                    playlist.RemoveTrack(toRemove);
            }

            var libraryCache = await libraryManager.LoadLibraryCacheAsync();
            libraryCache.Tracks = allTracks.ToList();
            await libraryManager.SaveLibraryCacheAsync(libraryCache);

            var recentlyPlayedCache = new LibraryManager.RecentlyPlayedCache
            {
                RecentlyPlayed = recentlyPlayed.Select(s => new LibraryManager.RecentlyPlayedItem { FilePath = s.FilePath, LastPlayed = s.LastPlayed }).ToList()
            };
            await libraryManager.SaveRecentlyPlayedAsync(recentlyPlayedCache);

            await libraryManager.SavePlaylistsFromCollectionAsync(playlists);

            UpdateUI();
            UpdateShuffledTracks();
            RefreshVisibleViews();
            UpdateStatusBar();
        }

        /// <summary>
        /// Updates the queue view with the current playing queue
        /// </summary>
        private void UpdateQueueView()
        {
            try
            {
                Console.WriteLine("UpdateQueueView called");

                var queueView = BuildQueueView();
                Console.WriteLine($"Built queue view with {queueView?.Count ?? 0} songs");

                if (queueViewControl != null)
                {
                    if (queueView != null && queueView.Count > 0)
                    {
                        Console.WriteLine($"Setting queue view ItemsSource to queue with {queueView.Count} songs");
                        queueViewControl.ItemsSource = queueView;
                    }
                    else
                    {
                        Console.WriteLine("Queue view is empty, setting ItemsSource to empty collection");
                        queueViewControl.ItemsSource = new ObservableCollection<Song>();
                    }
                }

                var currentQueue = GetCurrentPlayQueue();
                Console.WriteLine($"Current queue state - filteredTracks: {filteredTracks.Count}, shuffledTracks: {shuffledTracks.Count}, currentTrack: {currentTrack?.Title ?? "None"}, currentIndex: {GetCurrentTrackIndex()}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating queue view: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                if (queueViewControl != null) queueViewControl.ItemsSource = new ObservableCollection<Song>();
            }
        }

        /// <summary>
        /// Builds a proper queue view with currently playing song at the top
        /// </summary>
        private ObservableCollection<Song> BuildQueueView()
        {
            try
            {
                Console.WriteLine("BuildQueueView called");
                var queueView = new ObservableCollection<Song>();

                var currentQueue = GetCurrentPlayQueue();
                var currentIndex = GetCurrentTrackIndex();

                Console.WriteLine($"BuildQueueView - currentQueue: {currentQueue?.Count ?? 0}, currentIndex: {currentIndex}, currentTrack: {currentTrack?.Title ?? "None"}");

                if (currentQueue == null || currentQueue.Count == 0)
                {
                    Console.WriteLine("BuildQueueView - No queue available, returning empty queue");
                    return queueView;
                }

                if (currentTrack != null && currentIndex >= 0)
                {
                    Console.WriteLine($"BuildQueueView - Building queue with current track: {currentTrack.Title} at index {currentIndex}");

                    // Same structure for shuffle and ordered mode: current at top, then rest of active queue from currentIndex+1.
                    bool shuffle = titleBarPlayer.IsShuffleEnabled;
                    queueView.Add(currentTrack);
                    Console.WriteLine($"BuildQueueView - Added current track at top: {currentTrack.Title} (shuffle={shuffle})");

                    if (currentIndex < currentQueue.Count - 1)
                    {
                        for (int i = currentIndex + 1; i < currentQueue.Count; i++)
                        {
                            var track = currentQueue[i];
                            if (track != null && !string.IsNullOrEmpty(track.FilePath))
                            {
                                queueView.Add(track);
                                Console.WriteLine($"BuildQueueView - Added remaining track: {track.Title} at index {i} (shuffle={shuffle})");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"BuildQueueView - No remaining tracks after current track (shuffle={shuffle})");
                    }
                }
                else
                {
                    Console.WriteLine("BuildQueueView - No current track playing, returning empty queue (app is idle)");
                    return queueView;
                }

                Console.WriteLine($"Built queue view - current track: {currentTrack?.Title ?? "None"} (index {currentIndex}), total songs: {queueView.Count}, queue size: {currentQueue.Count}");
                return queueView;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error building queue view: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return new ObservableCollection<Song>();
            }
        }

        #endregion

        #region Music Management

        private async Task LoadMusicFromFolderAsync(string folderPath, bool saveToSettings = false)
        {
            try
            {
                var supportedExtensions = new[] { ".mp3", ".wav", ".flac", ".m4a", ".aac" };
                var musicFiles = await Task.Run(() =>
                {
                    return Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                        .Where(file => supportedExtensions.Contains(Path.GetExtension(file).ToLower()))
                        .ToList();
                });

                if (musicFiles.Count > 0)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        progressBarFill.Width = 0;
                        progressBarFill.Visibility = Visibility.Visible;
                    });
                }

                // Get existing tracks on UI thread before processing (ObservableCollection must be accessed on UI thread)
                var existingTracks = await Dispatcher.InvokeAsync(() =>
                {
                    return allTracks.Where(t => t.FilePath.StartsWith(folderPath)).ToList();
                });

                var newTracks = new List<Song>();
                int processedCount = 0;

                await Task.Run(async () =>
                {
                    foreach (var file in musicFiles)
                    {
                        try
                        {
                            var existingTrack = existingTracks.FirstOrDefault(t => t.FilePath == file);
                            if (existingTrack == null)
                            {
                                var track = TrackMetadataLoader.LoadSong(file);
                                if (track != null)
                                {
                                    await Dispatcher.InvokeAsync(() =>
                                    {
                                        newTracks.Add(track);
                                        allTracks.Add(track);
                                        filteredTracks.Add(track);
                                    });
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error loading {file}: {ex.Message}");
                        }
                        finally
                        {
                            processedCount++;
                            await Dispatcher.InvokeAsync(() =>
                            {
                                if (musicFiles.Count > 0)
                                {
                                    double progressPercent = (double)processedCount / musicFiles.Count;
                                    progressBarFill.Width = progressBarBackground.ActualWidth * progressPercent;
                                }
                                if (progressBarFill.Visibility == Visibility.Visible)
                                {
                                    UpdateStatusBar();
                                }
                            });
                            // Yield to allow UI thread to process updates
                            await Task.Yield();
                        }
                    }
                });

                if (saveToSettings)
                {
                    await libraryManager.AddMusicFolderAsync(folderPath);
                }

                await UpdateLibraryCacheAsync();

                await libraryManager.UpdateFolderScanTimeAsync(folderPath);

                Console.WriteLine($"After loading from folder - allTracks: {allTracks.Count}, filteredTracks: {filteredTracks.Count}");
                UpdateShuffledTracks();

                if (contentHost?.Content == queueViewControl)
                {
                    UpdateQueueView();
                }

                if (musicFiles.Count > 0)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        progressBarFill.Visibility = Visibility.Collapsed;
                        progressBarFill.Width = 0;
                        UpdateStatusBar();
                    });
                }
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    progressBarFill.Visibility = Visibility.Collapsed;
                    progressBarFill.Width = 0;
                });
                MessageDialog.Show(this, "Error", $"Error loading music folder: {ex.Message}", MessageDialog.Buttons.Ok);
            }
        }

        private async Task UpdateLibraryCacheAsync()
        {
            try
            {
                var libraryCache = await libraryManager.LoadLibraryCacheAsync();
                libraryCache.Tracks = allTracks.ToList();
                await libraryManager.SaveLibraryCacheAsync(libraryCache);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating library cache: {ex.Message}");
            }
        }

        #endregion

        #region Playback Control

        private void PlayTrack(Song track, object? requestSource = null)
        {
            try
            {
                if (track == null)
                {
                    Console.WriteLine("PlayTrack called with null track");
                    return;
                }

                if (string.IsNullOrEmpty(track.FilePath))
                {
                    Console.WriteLine($"PlayTrack called with track '{track.Title}' that has no file path");
                    return;
                }

                if (!File.Exists(track.FilePath))
                {
                    Console.WriteLine($"PlayTrack called with track '{track.Title}' but file doesn't exist: {track.FilePath}");
                    return;
                }

                Console.WriteLine($"Playing track: {track.Title} - {track.Artist}");
                TryInitializeAlbumContextQueue(requestSource, track);

                // Clean up existing audio objects without triggering PlaybackStopped
                // We need to do this manually to avoid resetting the current track
                try
                {
                    Console.WriteLine("Cleaning up audio objects...");

                    titleBarPlayer.IsPlaying = false;

                    if (waveOut != null)
                    {
                        Console.WriteLine("Removing PlaybackStopped event handler and stopping waveOut");
                        // Remove the event handler before stopping to prevent triggering PlaybackStopped
                        waveOut.PlaybackStopped -= WaveOut_PlaybackStopped;
                        waveOut.Stop();
                        waveOut.Dispose();
                        waveOut = null;
                        Console.WriteLine("waveOut disposed");
                    }

                    if (audioFileReader != null)
                    {
                        Console.WriteLine("Disposing audioFileReader");
                        audioFileReader.Dispose();
                        audioFileReader = null;
                        Console.WriteLine("audioFileReader disposed");
                    }

                    Console.WriteLine("Audio objects cleanup completed");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error during audio cleanup: {ex.Message}");
                }

                currentTrack = track;

                // Set the current track index in the active queue context.
                SyncCurrentTrackIndices(track);
                UpdateShuffleIndicesAfterTrackChange(track);

                var albumArt = AlbumArtLoader.LoadAlbumArt(track);

                titleBarPlayer.SetTrackInfo(track.Title, track.Artist, track.Album, albumArt);
                titleBarPlayer.SetAudioObjects(waveOut, audioFileReader);

                audioFileReader = new AudioFileReader(track.FilePath);
                waveOut = new WaveOutEvent();
                waveOut.PlaybackStopped += WaveOut_PlaybackStopped;
                waveOut.Init(audioFileReader);
                waveOut.Play();

                titleBarPlayer.SetAudioObjects(waveOut, audioFileReader);
                titleBarPlayer.IsPlaying = true;

                AddToRecentlyPlayed(track);

                RefreshVisibleViews();

                Console.WriteLine($"Successfully started playing: {track.Title}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing track '{track?.Title}': {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");

                try
                {
                    MessageDialog.Show(this, "Error", $"Error playing track: {ex.Message}", MessageDialog.Buttons.Ok);
                }
                catch
                {
                    // If showing message box fails, just log it
                    Console.WriteLine("Failed to show error message box");
                }

                try
                {
                    StopPlayback();
                }
                catch (Exception stopEx)
                {
                    Console.WriteLine($"Error stopping playback: {stopEx.Message}");
                }
            }
        }

        /// <summary>
        /// Loads a track without starting playback - useful for track navigation while preserving play state
        /// </summary>
        private void LoadTrackWithoutPlayback(Song track)
        {
            try
            {
                if (track == null)
                {
                    Console.WriteLine("LoadTrackWithoutPlayback called with null track");
                    return;
                }

                if (string.IsNullOrEmpty(track.FilePath))
                {
                    Console.WriteLine($"LoadTrackWithoutPlayback called with track '{track.Title}' that has no file path");
                    return;
                }

                if (!File.Exists(track.FilePath))
                {
                    Console.WriteLine($"LoadTrackWithoutPlayback called with track '{track.Title}' but file doesn't exist: {track.FilePath}");
                    return;
                }

                Console.WriteLine($"Loading track without playback: {track.Title} - {track.Artist}");

                bool wasPlaying = titleBarPlayer.IsPlaying;

                CleanupAudioObjects();

                currentTrack = track;

                // Set the current track index in the active queue context.
                SyncCurrentTrackIndices(track);
                UpdateShuffleIndicesAfterTrackChange(track);

                var albumArt = AlbumArtLoader.LoadAlbumArt(track);

                titleBarPlayer.SetTrackInfo(track.Title, track.Artist, track.Album, albumArt);

                audioFileReader = new AudioFileReader(track.FilePath);
                waveOut = new WaveOutEvent();
                waveOut.PlaybackStopped += WaveOut_PlaybackStopped;
                waveOut.Init(audioFileReader);

                titleBarPlayer.SetAudioObjects(waveOut, audioFileReader);

                titleBarPlayer.IsPlaying = wasPlaying;

                AddToRecentlyPlayed(track);

                RefreshVisibleViews();

                Console.WriteLine($"Successfully loaded track without playback: {track.Title}, playback state: {titleBarPlayer.IsPlaying}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading track '{track?.Title}' without playback: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");

                try
                {
                    MessageDialog.Show(this, "Error", $"Error loading track: {ex.Message}", MessageDialog.Buttons.Ok);
                }
                catch
                {
                    // If showing message box fails, just log it
                    Console.WriteLine("Failed to show error message box");
                }

                try
                {
                    StopPlayback();
                }
                catch (Exception stopEx)
                {
                    Console.WriteLine($"Error stopping playback: {stopEx.Message}");
                }
            }
        }

        private void AddToRecentlyPlayed(Song track)
        {
            track.MarkAsPlayed();

            var existing = recentlyPlayed.FirstOrDefault(t => t.FilePath == track.FilePath);
            if (existing != null)
            {
                recentlyPlayed.Remove(existing);
            }

            recentlyPlayed.Insert(0, track);

            while (recentlyPlayed.Count > 20)
            {
                recentlyPlayed.RemoveAt(recentlyPlayed.Count - 1);
            }
        }



        private void PausePlayback()
        {
            if (waveOut != null)
            {
                waveOut.Pause();
                titleBarPlayer.IsPlaying = false;
            }
        }

        private void ResumePlayback()
        {
            if (waveOut != null)
            {
                waveOut.Play();
                titleBarPlayer.IsPlaying = true;
            }
        }

        /// <summary>
        /// Safely resets the app to idle state (no track playing)
        /// </summary>
        private void ResetToIdleState()
        {
            try
            {
                Console.WriteLine("Resetting app to idle state...");

                currentTrack = null;
                currentTrackIndex = -1;
                currentShuffledIndex = -1;
                ClearContextualPlaybackQueue();

                titleBarPlayer.SetTrackInfo("No track selected", "", "");
                RefreshVisibleViews();

                Console.WriteLine("App reset to idle state successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error resetting to idle state: {ex.Message}");
            }
        }

        private bool HasContextualPlaybackQueue()
        {
            return contextualPlaybackQueue != null && contextualPlaybackQueue.Count > 0;
        }

        private void ClearContextualPlaybackQueue()
        {
            contextualPlaybackQueue = null;
            contextualPlaybackIndex = -1;
        }

        private bool IsPlaybackIdleAndQueueEmpty()
        {
            bool noTrackPlaying = currentTrack == null && !titleBarPlayer.IsPlaying;
            bool noQueueState = !HasContextualPlaybackQueue() && currentTrackIndex < 0 && currentShuffledIndex < 0;
            return noTrackPlaying && noQueueState;
        }

        private void TryInitializeAlbumContextQueue(object? requestSource, Song selectedTrack)
        {
            if (!ReferenceEquals(requestSource, albumsViewControl) || !IsPlaybackIdleAndQueueEmpty())
                return;

            string albumTitle = selectedTrack.Album ?? string.Empty;
            if (string.IsNullOrWhiteSpace(albumTitle))
                return;

            string selectedAlbumArtist = !string.IsNullOrWhiteSpace(selectedTrack.AlbumArtist)
                ? selectedTrack.AlbumArtist
                : selectedTrack.Artist ?? string.Empty;

            var albumTracks = allTracks
                .Where(s =>
                    string.Equals(s.Album, albumTitle, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        !string.IsNullOrWhiteSpace(s.AlbumArtist) ? s.AlbumArtist : s.Artist,
                        selectedAlbumArtist,
                        StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => int.TryParse(s.DiscNumber, out int d) ? d : 0)
                .ThenBy(s => s.TrackNumber)
                .ToList();

            if (albumTracks.Count == 0)
                return;

            contextualPlaybackQueue = new ObservableCollection<Song>(albumTracks);
            contextualPlaybackIndex = contextualPlaybackQueue.IndexOf(selectedTrack);
            Console.WriteLine($"Initialized album contextual queue with {contextualPlaybackQueue.Count} tracks at index {contextualPlaybackIndex}");
        }

        private void SyncCurrentTrackIndices(Song track)
        {
            if (HasContextualPlaybackQueue())
            {
                int contextIndex = contextualPlaybackQueue!.IndexOf(track);
                if (contextIndex >= 0)
                {
                    contextualPlaybackIndex = contextIndex;
                    currentTrackIndex = filteredTracks.IndexOf(track);
                    currentShuffledIndex = shuffledTracks.IndexOf(track);
                    return;
                }

                Console.WriteLine("Track not found in contextual queue; clearing contextual queue");
                ClearContextualPlaybackQueue();
            }

            currentTrackIndex = filteredTracks.IndexOf(track);
            currentShuffledIndex = shuffledTracks.IndexOf(track);
        }

        #endregion

        #region Playlist Management

        #endregion

        #region Settings Management

        #endregion





        protected override async void OnClosing(CancelEventArgs e)
        {
            try
            {
                appSettings.Player = new SettingsManager.PlayerSettings
                {
                    IsShuffleEnabled = titleBarPlayer.IsShuffleEnabled,
                    RepeatMode = titleBarPlayer.RepeatMode
                };

                // Save current window state - always save the normal window bounds, not the current maximized dimensions
                appSettings.WindowState = windowManager.GetCurrentWindowState();

                if (appSettings.WindowState != null)
                {
                    appSettings.WindowState.SidebarWidth = sidebarColumn.ActualWidth;
                }

                await settingsManager.SaveSettingsAsync(appSettings);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving settings on close: {ex.Message}");
            }

            StopPlayback();
            base.OnClosing(e);
        }
    }
}