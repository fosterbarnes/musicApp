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
using NAudio.Wave.SampleProviders;
using ATL;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using musicApp.Views;
using musicApp.Helpers;
using musicApp.Dialogs;
using musicApp.Constants;

namespace musicApp
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
        private ObservableCollection<Playlist> playlists = new ObservableCollection<Playlist>();
        private ObservableCollection<Song> recentlyPlayed = new ObservableCollection<Song>();

        private readonly object _libraryPathRegistryLock = new();
        private readonly HashSet<string> _registeredLibraryNormalizedPaths = new(StringComparer.OrdinalIgnoreCase);

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

        private bool _shutdownCloseFinalized;
        private bool _pendingLaunchSettings;
        private bool _pendingLaunchInfo;
        private bool _queuePopupProgrammaticClose;
        private bool _suppressQueuePopupToggleOpen;
        private string? _pendingLaunchSettingsSection;
        private string? _pendingLaunchInfoSection;
        private SettingsView? _settingsWindow;

        // ===========================================
        // AUDIO PLAYBACK STATE
        // ===========================================
        private IWavePlayer? waveOut;
        private AudioFileReader? audioFileReader;
        private VolumeSampleProvider? _sessionVolumeProvider;
        private Helpers.AudioOutputBackend _cachedAudioBackend = Helpers.AudioOutputBackend.WasapiShared;
        private bool _useSoftwareSessionVolume = true;
        private int _cachedOutputSampleRateHz = Helpers.PlaybackResampler.DefaultOutputSampleRateHz;
        private Helpers.PlaybackOutputBits _cachedOutputBits = Helpers.PlaybackOutputBitsUtil.Default;
        private bool _playbackOutputPrefsSyncedOnce;
        private Helpers.AudioOutputBackend _lastAppliedAudioBackend = Helpers.AudioOutputBackend.WasapiShared;
        private bool _lastAppliedUseSoftwareSessionVolume = true;
        private int _lastAppliedOutputSampleRateHz = Helpers.PlaybackResampler.DefaultOutputSampleRateHz;
        private Helpers.PlaybackOutputBits _lastAppliedOutputBits = Helpers.PlaybackOutputBitsUtil.Default;
        private int currentTrackIndex = -1;
        private int currentShuffledIndex = -1;
        private Song? currentTrack;
        private volatile bool isManuallyStopping;
        private volatile bool isManualNavigation;

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
        private const string MainViewLibrary = "Library";
        private const string MainViewQueue = "Queue";
        private const string MainViewPlaylists = "Playlists";
        private const string MainViewRecentlyPlayed = "RecentlyPlayed";
        private const string MainViewArtists = "Artists";
        private const string MainViewAlbums = "Albums";
        private const string MainViewRecentlyAdded = "RecentlyAdded";
        private const string MainViewGenres = "Genres";

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

            ApplySidebarPreferences();
            ApplyPlaybackPreferences();

            var launch = App.TakeLaunchPending();
            _pendingLaunchSettings = launch.OpenSettings;
            _pendingLaunchInfo = launch.OpenInfo;
            _pendingLaunchSettingsSection = launch.SettingsSection;
            _pendingLaunchInfoSection = launch.InfoSection;
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
            queueViewControl.TracksReordered += OnQueueTracksReordered;
            queueViewControl.QueueToolbarRemoveRequested += OnQueueToolbarRemoveRequested;
            queueViewControl.QueueToolbarMoveUpRequested += OnQueueToolbarMoveUpRequested;
            queueViewControl.QueueToolbarMoveDownRequested += OnQueueToolbarMoveDownRequested;
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

            ApplyInitialMainView(appSettings.LastActiveView);
        }

        private string GetCurrentMainViewKey()
        {
            if (ReferenceEquals(contentHost?.Content, queueViewControl))
                return MainViewQueue;
            if (ReferenceEquals(contentHost?.Content, playlistsViewControl))
                return MainViewPlaylists;
            if (ReferenceEquals(contentHost?.Content, recentlyPlayedViewControl))
                return MainViewRecentlyPlayed;
            if (ReferenceEquals(contentHost?.Content, artistsViewControl))
                return MainViewArtists;
            if (ReferenceEquals(contentHost?.Content, albumsViewControl) && albumsViewControl != null)
                return albumsViewControl.BrowseMode == AlbumsBrowseMode.RecentlyAdded
                    ? MainViewRecentlyAdded
                    : MainViewAlbums;
            if (ReferenceEquals(contentHost?.Content, genresViewControl))
                return MainViewGenres;
            return MainViewLibrary;
        }

        private void ApplyInitialMainView(string? savedViewKey)
        {
            if (string.Equals(savedViewKey, MainViewQueue, StringComparison.OrdinalIgnoreCase))
            {
                ShowQueueView();
                return;
            }
            if (string.Equals(savedViewKey, MainViewPlaylists, StringComparison.OrdinalIgnoreCase))
            {
                ShowPlaylistsView();
                return;
            }
            if (string.Equals(savedViewKey, MainViewRecentlyPlayed, StringComparison.OrdinalIgnoreCase))
            {
                ShowRecentlyPlayedView();
                return;
            }
            if (string.Equals(savedViewKey, MainViewArtists, StringComparison.OrdinalIgnoreCase))
            {
                ShowArtistsView();
                return;
            }
            if (string.Equals(savedViewKey, MainViewRecentlyAdded, StringComparison.OrdinalIgnoreCase))
            {
                ShowRecentlyAddedView();
                return;
            }
            if (string.Equals(savedViewKey, MainViewAlbums, StringComparison.OrdinalIgnoreCase))
            {
                ShowAlbumsView();
                return;
            }
            if (string.Equals(savedViewKey, MainViewGenres, StringComparison.OrdinalIgnoreCase))
            {
                ShowGenresView();
                return;
            }

            ShowLibraryView();
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

                if (_pendingLaunchSettings)
                {
                    _pendingLaunchSettings = false;
                    var settingsSection = _pendingLaunchSettingsSection;
                    _pendingLaunchSettingsSection = null;
                    ShowSettingsWindow(settingsSection);
                }

                if (_pendingLaunchInfo)
                {
                    _pendingLaunchInfo = false;
                    var infoSection = _pendingLaunchInfoSection;
                    _pendingLaunchInfoSection = null;
                    OpenLaunchInfoDialog(infoSection);
                }

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

            ResetLibraryPathRegistry();

            libraryCache ??= await libraryManager.LoadLibraryCacheAsync();

            var anyFolderDiskScan = false;
            foreach (var folderPath in musicFolders)
            {
                if (Directory.Exists(folderPath))
                {
                    bool hasNewFiles = await libraryManager.HasNewFilesInFolderAsync(folderPath);

                    if (hasNewFiles)
                    {
                        await LoadMusicFromFolderAsync(folderPath, true, false);
                        anyFolderDiskScan = true;
                    }
                    else
                    {
                        await LoadMusicFromCacheAsync(folderPath, libraryCache);
                    }
                }
            }

            if (anyFolderDiskScan)
                await MaybeRunPostScanSystemArtworkCacheAsync();
        }

        /// <summary>
        /// Loads music from cache for a specific folder
        /// </summary>
        private async Task LoadMusicFromCacheAsync(string folderPath, LibraryManager.LibraryCache? libraryCache = null)
        {
            try
            {
                libraryCache ??= await libraryManager.LoadLibraryCacheAsync();
                var cachedTracks = libraryCache.Tracks
                    .Where(t => !string.IsNullOrWhiteSpace(t.FilePath) && LibraryPathHelper.IsFileUnderMusicFolder(t.FilePath, folderPath))
                    .ToList();

                foreach (var track in cachedTracks)
                {
                    if (File.Exists(track.FilePath))
                    {
                        if (!TryRegisterLibraryPath(track.FilePath))
                            continue;

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
                        var updates = new List<(Song track, string path)>(tracksNeedingThumbnails.Count);
                        foreach (var t in tracksNeedingThumbnails)
                        {
                            var path = AlbumArtCacheManager.GenerateAndCache(t);
                            updates.Add((t, path));
                        }

                        await Dispatcher.InvokeAsync(async () =>
                        {
                            foreach (var (track, path) in updates)
                                track.ThumbnailCachePath = path;
                            await UpdateLibraryCacheAsync();
                        });
                    });
                }

                UpdateShuffledTracks();

                RefreshVisibleViews();

                await Dispatcher.InvokeAsync(() =>
                {
                    UpdateStatusBar();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading from cache: {ex.Message}");
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
            WindowFocusHelper.ScheduleActivate(this);
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
            WindowFocusHelper.ScheduleActivate(this);
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
            if (contentHost != null && ReferenceEquals(contentHost.Content, albumsViewControl))
                albumsViewControl?.RefreshAlbumGridFromLibrary();
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
                Debug.WriteLine($"Error updating status bar: {ex.Message}");
                if (statusBarText != null)
                {
                    statusBarText.Text = "Error calculating statistics";
                }
            }
        }

        private void UpdateStatusBarScanningLight(int processedFiles, int totalFiles)
        {
            if (statusBarText == null)
                return;
            statusBarText.Text = $"{allTracks.Count} songs, scanning files {processedFiles}/{totalFiles}…";
        }

        private void UpdateStatusBarPostScanAlbumWork(int done, int total)
        {
            if (statusBarText == null || total <= 0)
                return;
            statusBarText.Text = $"{allTracks.Count} songs, system artwork {done}/{total}…";
            if (progressBarBackground != null && progressBarFill != null)
            {
                progressBarFill.Visibility = Visibility.Visible;
                progressBarFill.Width = progressBarBackground.ActualWidth * (done / (double)total);
            }
        }

        private void SortLibraryTracksByPathForScan()
        {
            if (allTracks.Count <= 1)
                return;
            var sorted = allTracks
                .OrderBy(t => t.FilePath ?? "", StringComparer.OrdinalIgnoreCase)
                .ToList();
            allTracks.Clear();
            foreach (var t in sorted)
                allTracks.Add(t);
            filteredTracks.Clear();
            foreach (var t in sorted)
                filteredTracks.Add(t);
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
            if (titleBarPlayer.SearchBarBorder != null)
                titleBarPlayer.SearchBarBorder.AddHandler(
                    UIElement.MouseLeftButtonUpEvent,
                    new MouseButtonEventHandler(SearchBarBorder_MouseLeftButtonUp),
                    true);
            titleBarPlayer.QueuePopupToggleRequested += TitleBarPlayer_QueuePopupToggleRequested;
            if (titleBarPlayer.QueuePopupPlacementTarget is UIElement queuePlacementTarget)
                queuePlacementTarget.PreviewMouseLeftButtonDown += QueuePopupPlacementTarget_PreviewMouseLeftButtonDown;
            titleBarPlayer.PlaybackPositionCommitted += OnTitleBarPlaybackPositionCommitted;
            titleBarPlayer.VolumeChanged += TitleBarPlayer_VolumeChanged;
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

            queuePopup.Opened += QueuePopup_Opened;
            queuePopup.Closed += QueuePopup_Closed;

            if (queuePopupView != null)
            {
                queuePopupView.SongPlayRequested += QueuePopupView_SongPlayRequested;
                queuePopupView.QueueToolbarRemoveRequested += OnQueueToolbarRemoveRequested;
                queuePopupView.QueueToolbarMoveUpRequested += OnQueueToolbarMoveUpRequested;
                queuePopupView.QueueToolbarMoveDownRequested += OnQueueToolbarMoveDownRequested;
                queuePopupView.PlayNextRequested += OnPlayNextRequested;
                queuePopupView.AddToQueueRequested += OnAddToQueueRequested;
                queuePopupView.AddTrackToPlaylistRequested += OnAddTrackToPlaylistRequested;
                queuePopupView.CreateNewPlaylistWithTrackRequested += OnCreateNewPlaylistWithTrackRequested;
                queuePopupView.InfoRequested += OnInfoRequested;
                queuePopupView.ShowInArtistsRequested += OnShowInArtistsRequested;
                queuePopupView.ShowInSongsRequested += OnShowInSongsRequested;
                queuePopupView.ShowInAlbumsRequested += OnShowInAlbumsRequested;
                queuePopupView.ShowInQueueRequested += OnShowInQueueRequested;
                queuePopupView.ShowInExplorerRequested += OnShowInExplorerRequested;
                queuePopupView.RemoveFromLibraryRequested += OnRemoveFromLibraryRequested;
                queuePopupView.DeleteRequested += OnDeleteRequested;
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

        private void RefreshTrackListBindingsAndAlbumsView(Song? updatedTrack, bool allowInPlaceAlbumPatch)
        {
            songsView?.RefreshTrackListBindings();
            queueViewControl?.RefreshTrackListBindings();
            recentlyPlayedViewControl?.RefreshTrackListBindings();
            artistsViewControl?.RefreshTrackListBindings();
            genresViewControl?.RefreshTrackListBindings();

            if (contentHost != null && ReferenceEquals(contentHost.Content, albumsViewControl) && albumsViewControl != null)
            {
                if (!allowInPlaceAlbumPatch || updatedTrack == null || !albumsViewControl.TryRefreshAlbumGroupInPlace(updatedTrack))
                    albumsViewControl.RefreshAlbumGridFromLibrary();
            }

            playlistsViewControl?.RefreshTrackListBindings();
        }

        private void RefreshTitleBarFromCurrentTrack()
        {
            if (currentTrack == null)
                return;

            var albumArt = AlbumArtLoader.LoadAlbumArt(currentTrack);
            titleBarPlayer.SetTrackInfo(currentTrack.Title, currentTrack.Artist, currentTrack.Album, albumArt);
        }

        private void RefreshAfterMetadataEdit(Song updatedTrack)
        {
            RefreshTrackListBindingsAndAlbumsView(updatedTrack, allowInPlaceAlbumPatch: true);

            if (currentTrack != null && updatedTrack != null &&
                string.Equals(currentTrack.FilePath, updatedTrack.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                RefreshTitleBarFromCurrentTrack();
            }

            UpdateStatusBar();
        }

        private void RefreshAfterBatchMetadataEdit()
        {
            RefreshTrackListBindingsAndAlbumsView(updatedTrack: null, allowInPlaceAlbumPatch: false);
            RefreshTitleBarFromCurrentTrack();

            UpdateStatusBar();
        }

        private void ResetLibraryPathRegistry()
        {
            lock (_libraryPathRegistryLock)
            {
                _registeredLibraryNormalizedPaths.Clear();
            }
        }

        private bool TryRegisterLibraryPath(string? path)
        {
            var n = LibraryPathHelper.TryNormalizePath(path);
            if (n == null) return false;
            lock (_libraryPathRegistryLock)
            {
                if (_registeredLibraryNormalizedPaths.Contains(n)) return false;
                _registeredLibraryNormalizedPaths.Add(n);
                return true;
            }
        }

        private void ReleaseRegisteredLibraryPath(string? path)
        {
            var n = LibraryPathHelper.TryNormalizePath(path);
            if (n == null) return;
            lock (_libraryPathRegistryLock)
            {
                _registeredLibraryNormalizedPaths.Remove(n);
            }
        }

        private void UnregisterLibraryPathIfLastCopy(Song track)
        {
            var n = LibraryPathHelper.TryNormalizePath(track.FilePath);
            if (n == null) return;
            lock (_libraryPathRegistryLock)
            {
                foreach (var t in allTracks)
                {
                    if (ReferenceEquals(t, track)) continue;
                    if (LibraryPathHelper.PathsEqual(t.FilePath, track.FilePath)) return;
                }
                _registeredLibraryNormalizedPaths.Remove(n);
            }
        }

        private void RemoveTrackFromCollections(Song track, bool includeShuffled)
        {
            UnregisterLibraryPathIfLastCopy(track);
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

        private void SearchBarBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (searchPopup.IsOpen)
                return;
            var query = titleBarPlayer.GetSearchQuery();
            if (string.IsNullOrWhiteSpace(query))
                return;
            TitleBarPlayer_SearchTextChanged(this, query);
        }

        private void TitleBarPlayer_SearchTextChanged(object? sender, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                searchPopup.IsOpen = false;
                return;
            }
            CloseQueuePopupProgrammatically();
            var results = SearchHelper.Run(query, allTracks);
            if (searchPopupView != null)
                searchPopupView.Results = results;
            if (searchPopup.PlacementTarget == null && titleBarPlayer.SearchBarBorder != null)
                searchPopup.PlacementTarget = titleBarPlayer.SearchBarBorder;
            if (Mouse.Captured != null)
                Mouse.Capture(null);
            searchPopup.IsOpen = true;
            searchPopupView?.RefreshHeightForSearch();
        }

        private void QueuePopup_Opened(object? sender, EventArgs e)
        {
            _ = Dispatcher.BeginInvoke(new Action(CenterQueuePopupUnderPlacementButton), DispatcherPriority.Loaded);
        }

        private void QueuePopup_Closed(object? sender, EventArgs e)
        {
            if (_queuePopupProgrammaticClose)
            {
                _queuePopupProgrammaticClose = false;
                return;
            }

            if (titleBarPlayer.QueuePopupPlacementTarget is UIElement target)
            {
                var pos = Mouse.GetPosition(target);
                if (target.InputHitTest(pos) != null)
                    _suppressQueuePopupToggleOpen = true;
            }
        }

        private void CloseQueuePopupProgrammatically()
        {
            if (!queuePopup.IsOpen)
                return;
            _queuePopupProgrammaticClose = true;
            queuePopup.IsOpen = false;
            queuePopup.HorizontalOffset = 0;
        }

        private void CenterQueuePopupUnderPlacementButton()
        {
            if (!queuePopup.IsOpen || queuePopup.PlacementTarget is not FrameworkElement target ||
                queuePopup.Child is not FrameworkElement child)
                return;

            child.UpdateLayout();
            double popupW = child.ActualWidth;
            if (popupW <= 0 || double.IsNaN(popupW))
            {
                child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                popupW = Math.Max(child.DesiredSize.Width, child.MinWidth);
            }

            double tw = target.ActualWidth;
            if (tw <= 0 || double.IsNaN(tw))
                return;

            queuePopup.HorizontalOffset = (tw - popupW) / 2d;
        }

        private void QueuePopupPlacementTarget_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!queuePopup.IsOpen)
                return;
            CloseQueuePopupProgrammatically();
            e.Handled = true;
        }

        private void TitleBarPlayer_QueuePopupToggleRequested(object? sender, EventArgs e)
        {
            if (queuePopup.PlacementTarget == null && titleBarPlayer.QueuePopupPlacementTarget != null)
                queuePopup.PlacementTarget = titleBarPlayer.QueuePopupPlacementTarget;

            if (_suppressQueuePopupToggleOpen)
            {
                _suppressQueuePopupToggleOpen = false;
                return;
            }

            if (queuePopup.IsOpen)
            {
                CloseQueuePopupProgrammatically();
                return;
            }

            searchPopup.IsOpen = false;
            queuePopup.HorizontalOffset = 0;
            if (queuePopupView != null)
                queuePopupView.QueueTracks = BuildQueueView();
            queuePopup.IsOpen = true;
            queuePopupView?.RefreshHeight();
        }

        private void SearchPopupView_SongSelected(object? sender, Song song)
        {
            searchPopup.IsOpen = false;
            PlayTrack(song);
        }

        private void QueuePopupView_SongPlayRequested(object? sender, Song song)
        {
            PlayTrack(song, queuePopupView);
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
            if (albumsViewControl != null)
                albumsViewControl.BrowseMode = AlbumsBrowseMode.AllAlbums;
            ShowAlbumsView(bindFullLibrary: false);
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

        private void RegenerateShuffledTracks()
        {
            try
            {
                if (HasContextualPlaybackQueue())
                    return;

                if (filteredTracks == null || filteredTracks.Count == 0)
                {
                    shuffledTracks.Clear();
                    currentShuffledIndex = -1;
                    return;
                }

                shuffledTracks.Clear();

                foreach (var track in filteredTracks)
                {
                    if (track != null && !string.IsNullOrEmpty(track.FilePath))
                        shuffledTracks.Add(track);
                }

                if (!titleBarPlayer.IsShuffleEnabled)
                {
                    currentShuffledIndex = currentTrack != null ? filteredTracks.IndexOf(currentTrack) : -1;
                    if (contentHost?.Content == queueViewControl)
                        UpdateQueueView();
                    return;
                }

                if (currentTrack != null)
                {
                    int li = filteredTracks.IndexOf(currentTrack);
                    currentTrackIndex = li;
                    currentShuffledIndex = li;
                    if (li < 0)
                    {
                        ShuffleRangeUntilOrderDiffersFromLinear(shuffledTracks, filteredTracks, 0, shuffledTracks.Count - 1);
                        currentShuffledIndex = 0;
                    }
                    else if (li < shuffledTracks.Count - 1)
                    {
                        ShuffleRangeUntilOrderDiffersFromLinear(shuffledTracks, filteredTracks, li + 1, shuffledTracks.Count - 1);
                    }
                }
                else
                {
                    currentShuffledIndex = -1;
                    if (shuffledTracks.Count > 1)
                        ShuffleRangeUntilOrderDiffersFromLinear(shuffledTracks, filteredTracks, 0, shuffledTracks.Count - 1);
                }

                if (contentHost?.Content == queueViewControl)
                    UpdateQueueView();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RegenerateShuffledTracks: {ex}");

                try
                {
                    shuffledTracks.Clear();
                    currentShuffledIndex = -1;
                }
                catch (Exception clearEx)
                {
                    Debug.WriteLine($"RegenerateShuffledTracks clear: {clearEx.Message}");
                }
            }
        }

        private void ResyncLibraryShuffledUpcomingOrder(Song track)
        {
            if (track == null || filteredTracks == null)
                return;

            if (shuffledTracks.Count != filteredTracks.Count)
            {
                RegenerateShuffledTracks();
                return;
            }

            int li = filteredTracks.IndexOf(track);
            currentTrackIndex = li;
            currentShuffledIndex = li;
            if (li < 0)
                return;

            for (int i = 0; i <= li && i < shuffledTracks.Count; i++)
                shuffledTracks[i] = filteredTracks[i];

            if (li >= shuffledTracks.Count - 1)
                return;

            var remaining = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int r = li + 1; r < filteredTracks.Count; r++)
            {
                var s = filteredTracks[r];
                if (!string.IsNullOrWhiteSpace(s?.FilePath))
                    remaining.Add(s.FilePath);
            }

            var tail = new List<Song>();
            for (int i = li + 1; i < shuffledTracks.Count; i++)
            {
                var s = shuffledTracks[i];
                if (s != null && !string.IsNullOrWhiteSpace(s.FilePath) && remaining.Contains(s.FilePath))
                    tail.Add(s);
            }

            if (tail.Count < remaining.Count)
            {
                var inTail = new HashSet<string>(tail.Select(s => s.FilePath), StringComparer.OrdinalIgnoreCase);
                for (int r = li + 1; r < filteredTracks.Count; r++)
                {
                    var s = filteredTracks[r];
                    if (s != null && !string.IsNullOrWhiteSpace(s.FilePath) && !inTail.Contains(s.FilePath))
                    {
                        tail.Add(s);
                        inTail.Add(s.FilePath);
                    }
                }
            }

            int k = 0;
            for (int i = li + 1; i < shuffledTracks.Count && k < tail.Count; i++, k++)
                shuffledTracks[i] = tail[k];
        }

        private void EnsureShuffledTracksInitialized()
        {
            try
            {
                if (!titleBarPlayer.IsShuffleEnabled)
                {
                    return;
                }

                if (shuffledTracks.Count == 0 || shuffledTracks.Count != filteredTracks.Count)
                {
                    RegenerateShuffledTracks();
                    return;
                }

                if (currentTrack != null && shuffledTracks.IndexOf(currentTrack) == -1)
                {
                    RegenerateShuffledTracks();
                    return;
                }

                if (currentTrack != null && currentShuffledIndex == -1)
                {
                    currentShuffledIndex = shuffledTracks.IndexOf(currentTrack);
                    if (currentShuffledIndex == -1)
                        RegenerateShuffledTracks();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsureShuffledTracksInitialized: {ex.Message}");
                RegenerateShuffledTracks();
            }
        }

        private void UpdateShuffledTracks()
        {
            if (titleBarPlayer.IsShuffleEnabled)
            {
                if (shuffledTracks.Count != filteredTracks.Count)
                    RegenerateShuffledTracks();
                else
                    EnsureShuffledTracksInitialized();
            }
        }

        private void UpdateShuffleIndicesAfterTrackChange(Song track)
        {
            if (!titleBarPlayer.IsShuffleEnabled || HasContextualPlaybackQueue())
                return;

            ResyncLibraryShuffledUpcomingOrder(track);
        }

        private ObservableCollection<Song> GetCurrentPlayQueue()
        {
            try
            {
                if (contextualPlaybackFuture != null && contextualPlaybackFuture.Count > 0)
                    return contextualPlaybackFuture;

                var queue = titleBarPlayer.IsShuffleEnabled ? shuffledTracks : filteredTracks;

                if (queue == null)
                    queue = filteredTracks;

                return queue ?? new ObservableCollection<Song>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetCurrentPlayQueue: {ex.Message}");
                return filteredTracks ?? new ObservableCollection<Song>();
            }
        }

        private int GetCurrentTrackIndex()
        {
            try
            {
                if (contextualPlaybackFuture != null && contextualPlaybackFuture.Count > 0)
                    return 0;

                return titleBarPlayer.IsShuffleEnabled ? currentShuffledIndex : currentTrackIndex;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetCurrentTrackIndex: {ex.Message}");
                return -1;
            }
        }

        private void SetCurrentTrackIndex(int index)
        {
            if (contextualPlaybackFuture != null && contextualPlaybackFuture.Count > 0)
                return;

            if (titleBarPlayer.IsShuffleEnabled)
            {
                currentShuffledIndex = index;
            }
            else
            {
                currentTrackIndex = index;
            }
        }

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
                }
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetTrackFromCurrentQueue({index}): {ex.Message}");
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

        private PreviousTrackSeekBehavior GetPreviousTrackSeekBehavior(double elapsedSeconds, int currentIndex)
        {
            if (elapsedSeconds >= UILayoutConstants.PreviousTrackRestartThresholdSeconds)
                return PreviousTrackSeekBehavior.RestartCurrent;

            bool canGoPrevious = currentIndex > 0 ||
                                 (HasContextualPlaybackQueue() && contextualPlaybackHistoryMru.Count > 0);

            if (elapsedSeconds <= UILayoutConstants.PreviousTrackEdgeThresholdSeconds && canGoPrevious)
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

        private void TitleBarPlayer_VolumeChanged(object? sender, double volume0To100)
        {
            if (!_useSoftwareSessionVolume || _sessionVolumeProvider == null)
                return;
            if (titleBarPlayer.IsMuted)
                _sessionVolumeProvider.Volume = 0f;
            else
                _sessionVolumeProvider.Volume = (float)(volume0To100 / 100.0);
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

            isManualNavigation = true;

            var currentPosition = titleBarPlayer.CurrentPosition;

            bool wasPlaying = titleBarPlayer.IsPlaying;
            var behavior = GetPreviousTrackSeekBehavior(currentPosition.TotalSeconds, currentIndex);

            switch (behavior)
            {
                case PreviousTrackSeekBehavior.RestartCurrent:
                    RestartCurrentTrackFromPreviousButton(wasPlaying);
                    break;

                case PreviousTrackSeekBehavior.GoToPrevious:
                    if (HasContextualPlaybackQueue())
                    {
                        if (TryRewindContextualSessionOne(out var prevContext) && prevContext != null)
                        {
                            LoadTrackWithoutPlayback(prevContext);
                            if (wasPlaying)
                                ResumePlayback();
                        }
                        else if (contentHost?.Content == queueViewControl)
                            UpdateQueueView();
                    }
                    else
                    {
                        var previousTrack = GetTrackFromCurrentQueue(currentIndex - 1);
                        if (previousTrack != null)
                        {
                            LoadTrackWithoutPlayback(previousTrack);
                            if (wasPlaying)
                                ResumePlayback();
                        }
                        else if (contentHost?.Content == queueViewControl)
                            UpdateQueueView();
                    }
                    break;

                case PreviousTrackSeekBehavior.RestartCurrentEdge:
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

            isManualNavigation = true;

            if (HasContextualPlaybackQueue())
            {
                if (TryManualAdvanceContextualSession())
                {
                    var next = contextualPlaybackFuture![0];
                    bool wasPlaying = titleBarPlayer.IsPlaying;
                    LoadTrackWithoutPlayback(next);
                    if (wasPlaying)
                        ResumePlayback();
                }
                else
                    ResetPlaybackToIdleAndRefreshQueue();

                Task.Delay(UILayoutConstants.ManualNavigationResetDelayMs).ContinueWith(_ => isManualNavigation = false);
                return;
            }

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
                    ResetPlaybackToIdleAndRefreshQueue();
                }
            }
            else
            {
                ResetPlaybackToIdleAndRefreshQueue();
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
                    Debug.WriteLine($"Error saving sidebar width: {ex.Message}");
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
            if (HasContextualPlaybackQueue())
            {
                RebuildContextualDisplayFromLinear();
            }
            else if (isShuffleEnabled)
            {
                RegenerateShuffledTracks();
            }
            else
            {
                shuffledTracks.Clear();
                foreach (var t in filteredTracks)
                    shuffledTracks.Add(t);
                if (currentTrack != null)
                {
                    currentTrackIndex = filteredTracks.IndexOf(currentTrack);
                    currentShuffledIndex = currentTrackIndex;
                    if (currentTrackIndex == -1)
                        currentTrackIndex = 0;
                }
                else
                {
                    currentShuffledIndex = -1;
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

            if (HasContextualPlaybackQueue() &&
                contextualPlaybackFuture != null &&
                contextualLinearFuture.Count == contextualPlaybackFuture.Count)
            {
                const int insertIdx = 1;
                if (insertIdx > contextualLinearFuture.Count)
                {
                    contextualLinearFuture.Add(track);
                    contextualPlaybackFuture.Add(track);
                }
                else
                {
                    contextualLinearFuture.Insert(insertIdx, track);
                    contextualPlaybackFuture.Insert(insertIdx, track);
                }

                RefreshVisibleViews();
                return;
            }

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

            if (HasContextualPlaybackQueue() &&
                contextualPlaybackFuture != null &&
                contextualLinearFuture.Count == contextualPlaybackFuture.Count)
            {
                contextualLinearFuture.Add(track);
                contextualPlaybackFuture.Add(track);
                RefreshVisibleViews();
                return;
            }

            filteredTracks.Add(track);
            shuffledTracks.Add(track);

            RefreshVisibleViews();
        }

        private void OnQueueToolbarRemoveRequested(object? sender, EventArgs e)
        {
            int viewIndex = -1;
            if (queuePopupView != null && ReferenceEquals(sender, queuePopupView))
                viewIndex = queuePopupView.GetSelectedViewIndex();
            else if (queueViewControl != null)
                viewIndex = queueViewControl.GetSelectedViewIndex();

            if (viewIndex < 0)
                return;

            var queue = GetCurrentPlayQueue();
            int baseIdx = GetCurrentTrackIndex();
            if (queue == null || baseIdx < 0 || queue.Count == 0)
                return;

            int q = baseIdx + viewIndex;
            if (q < 0 || q >= queue.Count)
                return;

            Song? removed = queue[q];
            bool removeWasCurrent = viewIndex == 0 && currentTrack != null && removed != null &&
                ((!string.IsNullOrWhiteSpace(removed.FilePath) &&
                  !string.IsNullOrWhiteSpace(currentTrack.FilePath) &&
                  string.Equals(removed.FilePath, currentTrack.FilePath, StringComparison.OrdinalIgnoreCase)) ||
                 ReferenceEquals(currentTrack, removed));

            bool wasPlaying = titleBarPlayer.IsPlaying;

            queue.RemoveAt(q);

            if (HasContextualPlaybackQueue() &&
                q >= 0 &&
                q < contextualLinearFuture.Count)
                contextualLinearFuture.RemoveAt(q);

            if (removeWasCurrent)
            {
                if (baseIdx < queue.Count)
                {
                    var next = queue[baseIdx];
                    if (wasPlaying)
                        PlayTrack(next, null);
                    else
                        LoadTrackWithoutPlayback(next);
                }
                else
                    StopPlayback();
            }
            else if (currentTrack != null)
                SyncCurrentTrackIndices(currentTrack);

            UpdateQueueView();
            RefreshVisibleViews();
        }

        private void OnQueueToolbarMoveUpRequested(object? sender, EventArgs e)
        {
            int ix = queuePopupView != null && ReferenceEquals(sender, queuePopupView)
                ? queuePopupView.GetSelectedViewIndex()
                : queueViewControl?.GetSelectedViewIndex() ?? -1;
            if (ix < 2)
                return;
            OnQueueTracksReordered(this, (ix, ix - 1));
        }

        private void OnQueueToolbarMoveDownRequested(object? sender, EventArgs e)
        {
            var playQ = GetCurrentPlayQueue();
            int baseIdx = GetCurrentTrackIndex();
            int ix = queuePopupView != null && ReferenceEquals(sender, queuePopupView)
                ? queuePopupView.GetSelectedViewIndex()
                : queueViewControl?.GetSelectedViewIndex() ?? -1;
            if (playQ == null || baseIdx < 0 || ix < 1)
                return;
            int viewCount = playQ.Count - baseIdx;
            if (ix >= viewCount - 1)
                return;
            OnQueueTracksReordered(this, (ix, ix + 1));
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
        /// Removes the track from the musicApp library (in-memory and persisted). Does not delete the file.
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

        private void UpdateQueueView()
        {
            try
            {
                var queueView = BuildQueueView();

                if (queueViewControl != null)
                {
                    if (queueView != null && queueView.Count > 0)
                        queueViewControl.ItemsSource = queueView;
                    else
                        queueViewControl.ItemsSource = new ObservableCollection<Song>();
                }

                if (queuePopup.IsOpen && queuePopupView != null)
                {
                    queuePopupView.QueueTracks = queueView ?? new ObservableCollection<Song>();
                    queuePopupView.RefreshHeight();
                    _ = Dispatcher.BeginInvoke(new Action(CenterQueuePopupUnderPlacementButton), DispatcherPriority.ContextIdle);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateQueueView: {ex}");
                if (queueViewControl != null) queueViewControl.ItemsSource = new ObservableCollection<Song>();
            }
        }

        private ObservableCollection<Song> BuildQueueView()
        {
            try
            {
                var queueView = new ObservableCollection<Song>();

                var currentQueue = GetCurrentPlayQueue();
                var currentIndex = GetCurrentTrackIndex();

                if (currentQueue == null || currentQueue.Count == 0)
                    return queueView;

                if (currentTrack != null && currentIndex >= 0)
                {
                    queueView.Add(currentTrack);

                    if (currentIndex < currentQueue.Count - 1)
                    {
                        for (int i = currentIndex + 1; i < currentQueue.Count; i++)
                        {
                            var track = currentQueue[i];
                            if (track != null && !string.IsNullOrEmpty(track.FilePath))
                                queueView.Add(track);
                        }
                    }
                }

                return queueView;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"BuildQueueView: {ex}");
                return new ObservableCollection<Song>();
            }
        }

        #endregion

        #region Music Management

        private async Task LoadMusicFromFolderAsync(string folderPath, bool saveToSettings = false, bool runPostScanSystemArtworkIfEnabled = false)
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

                var scanStopwatch = Stopwatch.StartNew();

                int processedCount = 0;
                int scanConcurrencySmoothed = 0;
                const int scanBatchSize = 320;
                const int scanUiFlushEvery = 22;

                var pendingScanTracks = new List<Song>();
                var pendingScanLock = new object();
                SystemResourceSnapshot? lastResourceSnapshot = null;
                var batchesSinceSample = int.MaxValue;
                var batchIndex = 0;
                var scanConcurrentWorkers = 0;
                var scanPeakWorkersBatch = 0;

                static void ScanRecordPeakConcurrent(ref int peakField, int currentConcurrent)
                {
                    int oldPeak, newPeak;
                    do
                    {
                        oldPeak = Volatile.Read(ref peakField);
                        if (currentConcurrent <= oldPeak)
                            return;
                        newPeak = currentConcurrent;
                    } while (Interlocked.CompareExchange(ref peakField, newPeak, oldPeak) != oldPeak);
                }

                async Task FlushScanProgressAsync(int done)
                {
                    List<Song> toAdd;
                    lock (pendingScanLock)
                    {
                        if (pendingScanTracks.Count == 0)
                            toAdd = new List<Song>();
                        else
                        {
                            toAdd = new List<Song>(pendingScanTracks);
                            pendingScanTracks.Clear();
                        }
                    }

                    await Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var t in toAdd)
                        {
                            allTracks.Add(t);
                            filteredTracks.Add(t);
                        }

                        if (musicFiles.Count > 0)
                        {
                            double progressPercent = done / (double)musicFiles.Count;
                            progressBarFill.Width = progressBarBackground.ActualWidth * progressPercent;
                        }

                        if (progressBarFill.Visibility == Visibility.Visible)
                            UpdateStatusBarScanningLight(done, musicFiles.Count);
                    });
                }

                var scanTotalBatches = musicFiles.Count == 0
                    ? 0
                    : (musicFiles.Count + scanBatchSize - 1) / scanBatchSize;

                foreach (var batch in musicFiles.Chunk(scanBatchSize))
                {
                    scanPeakWorkersBatch = 0;
                    var mustSample = batchIndex == 0 || batchesSinceSample >= 2;

                    int dop;
                    if (mustSample)
                    {
                        var sampleInterval = lastResourceSnapshot.HasValue && lastResourceSnapshot.Value.CpuBusyPercent < 40
                            ? TimeSpan.FromMilliseconds(50)
                            : TimeSpan.FromMilliseconds(100);
                        lastResourceSnapshot = await Task.Run(() => WindowsSystemMetrics.Sample(sampleInterval));
                        dop = ScanConcurrencyAdvisor.Recommend(
                            lastResourceSnapshot.Value,
                            Environment.ProcessorCount,
                            ref scanConcurrencySmoothed);
                        var batchNo = batchIndex + 1;
                        Debug.WriteLine(
                            $"[LibraryScan] Chunk {batchNo}/{scanTotalBatches}: about {batch.Length} files — took a fresh PC reading ({sampleInterval.TotalMilliseconds:F0} ms). " +
                            $"Will scan up to {dop} at the same time.");
                        batchesSinceSample = 0;
                    }
                    else
                    {
                        dop = scanConcurrencySmoothed;
                        var batchNo = batchIndex + 1;
                        Debug.WriteLine(
                            $"[LibraryScan] Chunk {batchNo}/{scanTotalBatches}: about {batch.Length} files — skipped PC re-check (same limits). " +
                            $"Still up to {dop} at the same time.");
                    }

                    var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = dop };

                    await Parallel.ForEachAsync(batch, parallelOptions, async (file, _) =>
                    {
                        var concurrentNow = Interlocked.Increment(ref scanConcurrentWorkers);
                        ScanRecordPeakConcurrent(ref scanPeakWorkersBatch, concurrentNow);
                        Song? track = null;
                        try
                        {
                            if (!TryRegisterLibraryPath(file))
                            {
                            }
                            else
                            {
                                track = TrackMetadataLoader.LoadSong(file);
                                if (track == null)
                                    ReleaseRegisteredLibraryPath(file);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error loading {file}: {ex.Message}");
                            ReleaseRegisteredLibraryPath(file);
                            track = null;
                        }
                        finally
                        {
                            Interlocked.Decrement(ref scanConcurrentWorkers);
                        }

                        var done = Interlocked.Increment(ref processedCount);
                        if (track != null)
                        {
                            lock (pendingScanLock)
                                pendingScanTracks.Add(track);
                        }

                        if (done % scanUiFlushEvery == 0)
                            await FlushScanProgressAsync(done);
                    });

                    var doneBatchNo = batchIndex + 1;
                    Debug.WriteLine(
                        $"[LibraryScan] Chunk {doneBatchNo}/{scanTotalBatches} finished — {processedCount} of {musicFiles.Count} files touched so far. " +
                        $"Peak parallel work: {scanPeakWorkersBatch} (limit {dop}).");

                    if (!mustSample)
                        batchesSinceSample++;
                    batchIndex++;
                }

                await FlushScanProgressAsync(processedCount);

                await Dispatcher.InvokeAsync(SortLibraryTracksByPathForScan);

                scanStopwatch.Stop();
                Debug.WriteLine($"[LibraryScan] Scan complete ({scanStopwatch.Elapsed.TotalSeconds:F2} seconds)");

                if (saveToSettings)
                {
                    await libraryManager.AddMusicFolderAsync(folderPath);
                }

                await UpdateLibraryCacheAsync();

                await libraryManager.UpdateFolderScanTimeAsync(folderPath);

                UpdateShuffledTracks();

                if (contentHost?.Content == queueViewControl)
                {
                    UpdateQueueView();
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    UpdateUI();
                    if (musicFiles.Count > 0)
                    {
                        if (!runPostScanSystemArtworkIfEnabled)
                        {
                            progressBarFill.Visibility = Visibility.Collapsed;
                            progressBarFill.Width = 0;
                        }
                        UpdateStatusBar();
                    }
                });

                if (runPostScanSystemArtworkIfEnabled)
                    await MaybeRunPostScanSystemArtworkCacheAsync().ConfigureAwait(true);
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
                Debug.WriteLine($"Error updating library cache: {ex.Message}");
            }
        }

        #endregion

        #region Playback Control

        private IWaveProvider BuildPlaybackOutput(AudioFileReader reader, string tagSourcePath)
        {
            _ = tagSourcePath;
            return PlaybackResampler.ToOutputWaveProvider(reader, _cachedOutputSampleRateHz);
        }

        private void PlayTrack(Song track, object? requestSource = null)
        {
            try
            {
                if (track == null)
                    return;

                if (string.IsNullOrEmpty(track.FilePath))
                    return;

                if (!File.Exists(track.FilePath))
                    return;

                TryInitializeContextFromPlayTrack(requestSource, track);

                try
                {
                    titleBarPlayer.IsPlaying = false;
                    TeardownCrossfadePlaybackState();

                    if (waveOut != null)
                    {
                        waveOut.PlaybackStopped -= WaveOut_PlaybackStopped;
                        waveOut.Stop();
                        waveOut.Dispose();
                        waveOut = null;
                    }

                    if (audioFileReader != null)
                    {
                        audioFileReader.Dispose();
                        audioFileReader = null;
                    }

                    ClearCrossfadeMixerReferences();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"PlayTrack audio cleanup: {ex.Message}");
                }

                currentTrack = track;

                // Set the current track index in the active queue context.
                SyncCurrentTrackIndices(track, requestSource);
                UpdateShuffleIndicesAfterTrackChange(track);

                var albumArt = AlbumArtLoader.LoadAlbumArt(track);

                titleBarPlayer.SetTrackInfo(track.Title, track.Artist, track.Album, albumArt);
                TitleBarSetAudioObjects(waveOut, audioFileReader);

                RefreshPlaybackAudioPreferenceFields();
                audioFileReader = new AudioFileReader(track.FilePath);
                waveOut = AudioOutputDeviceFactory.Create(_cachedAudioBackend);
                waveOut.PlaybackStopped += WaveOut_PlaybackStopped;
                waveOut.Init(CreatePlaybackInitChain(audioFileReader, track.FilePath));
                waveOut.Play();

                TitleBarSetAudioObjects(waveOut, audioFileReader);
                titleBarPlayer.IsPlaying = true;
                _crossfadeOverlapStartedForThisOutgoing = false;
                EnsureCrossfadePollTimer();

                AddToRecentlyPlayed(track);

                RefreshVisibleViews();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PlayTrack '{track?.Title}': {ex}");

                try
                {
                    MessageDialog.Show(this, "Error", $"Error playing track: {ex.Message}", MessageDialog.Buttons.Ok);
                }
                catch
                {
                    Debug.WriteLine("PlayTrack: failed to show error dialog");
                }

                try
                {
                    StopPlayback();
                }
                catch (Exception stopEx)
                {
                    Debug.WriteLine($"PlayTrack StopPlayback: {stopEx.Message}");
                }
            }
        }

        private void LoadTrackWithoutPlayback(Song track)
        {
            try
            {
                if (track == null)
                    return;

                if (string.IsNullOrEmpty(track.FilePath))
                    return;

                if (!File.Exists(track.FilePath))
                    return;

                bool wasPlaying = titleBarPlayer.IsPlaying;

                CleanupAudioObjects();

                currentTrack = track;

                // Set the current track index in the active queue context.
                SyncCurrentTrackIndices(track);
                UpdateShuffleIndicesAfterTrackChange(track);

                var albumArt = AlbumArtLoader.LoadAlbumArt(track);

                titleBarPlayer.SetTrackInfo(track.Title, track.Artist, track.Album, albumArt);

                RefreshPlaybackAudioPreferenceFields();
                audioFileReader = new AudioFileReader(track.FilePath);
                waveOut = AudioOutputDeviceFactory.Create(_cachedAudioBackend);
                waveOut.PlaybackStopped += WaveOut_PlaybackStopped;
                waveOut.Init(CreatePlaybackInitChain(audioFileReader, track.FilePath));

                TitleBarSetAudioObjects(waveOut, audioFileReader);

                titleBarPlayer.IsPlaying = wasPlaying;
                _crossfadeOverlapStartedForThisOutgoing = false;
                EnsureCrossfadePollTimer();

                AddToRecentlyPlayed(track);

                RefreshVisibleViews();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadTrackWithoutPlayback '{track?.Title}': {ex}");

                try
                {
                    MessageDialog.Show(this, "Error", $"Error loading track: {ex.Message}", MessageDialog.Buttons.Ok);
                }
                catch
                {
                    Debug.WriteLine("LoadTrackWithoutPlayback: failed to show error dialog");
                }

                try
                {
                    StopPlayback();
                }
                catch (Exception stopEx)
                {
                    Debug.WriteLine($"LoadTrackWithoutPlayback StopPlayback: {stopEx.Message}");
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

        private void ResetToIdleState()
        {
            try
            {
                currentTrack = null;
                currentTrackIndex = -1;
                currentShuffledIndex = -1;
                ClearContextualPlaybackQueue();

                titleBarPlayer.SetTrackInfo("No track selected", "", "");
                RefreshVisibleViews();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ResetToIdleState: {ex.Message}");
            }
        }

        private bool IsPlaybackIdleAndQueueEmpty()
        {
            bool noTrackPlaying = currentTrack == null && !titleBarPlayer.IsPlaying;
            bool noQueueState = !HasContextualPlaybackQueue() && currentTrackIndex < 0 && currentShuffledIndex < 0;
            return noTrackPlaying && noQueueState;
        }

        #endregion


        protected override void OnClosing(CancelEventArgs e)
        {
            if (_shutdownCloseFinalized)
            {
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;
            _ = FinishShutdownAsync();
        }

        private async Task FinishShutdownAsync()
        {
            try
            {
                try
                {
                    appSettings.LastActiveView = GetCurrentMainViewKey();

                    appSettings.Player = new SettingsManager.PlayerSettings
                    {
                        IsShuffleEnabled = titleBarPlayer.IsShuffleEnabled,
                        RepeatMode = titleBarPlayer.RepeatMode,
                        TitleBarVolume0To100 = titleBarPlayer.Volume
                    };

                    appSettings.WindowState = windowManager.GetCurrentWindowState();

                    if (appSettings.WindowState != null)
                    {
                        appSettings.WindowState.SidebarWidth = sidebarColumn.ActualWidth;
                    }

                    await settingsManager.SaveSettingsAsync(appSettings).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error saving settings on close: {ex.Message}");
                }

                StopPlayback();
            }
            finally
            {
                _shutdownCloseFinalized = true;
                try { await Dispatcher.InvokeAsync(() => Close()); }
                catch { /* ignore */ }
            }
        }
    }
}