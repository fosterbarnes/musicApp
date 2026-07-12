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
        private BulkObservableCollection<Song> allTracks = new BulkObservableCollection<Song>();
        private BulkObservableCollection<Song> filteredTracks = new BulkObservableCollection<Song>();
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
        private DispatcherTimer? _volumeSaveTimer;
        private double _pendingVolume0To100 = 100;

        private bool _shutdownCloseFinalized;
        private bool _pendingLaunchSettings;
        private bool _pendingLaunchInfo;
        private bool _queuePopupProgrammaticClose;
        private bool _suppressQueuePopupToggleOpen;
        private string? _pendingLaunchSettingsSection;
        private string? _pendingLaunchInfoSection;
        private SettingsView? _settingsWindow;

        // ===========================================
        // HOTKEY MANAGEMENT
        // ===========================================
        private Hotkey.LocalHotkeys _localHotkeys;
        private Hotkey.GlobalHotkeys _globalHotkeys;

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

            _localHotkeys = new Hotkey.LocalHotkeys(this);
            _globalHotkeys = new Hotkey.GlobalHotkeys(this);

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
            InitAlbumsView();
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

        private bool _otherViewsCreated;

        private void InitAlbumsView()
        {
            albumsViewControl = new AlbumsView();
            albumsViewControl.PlayTrackRequested += (s, track) => PlayTrack(track, s);
            albumsViewControl.PlayNextRequested += OnPlayNextRequested;
            albumsViewControl.AddToQueueRequested += OnAddToQueueRequested;
            albumsViewControl.AddTrackToPlaylistRequested += OnAddTrackToPlaylistRequested;
            albumsViewControl.CreateNewPlaylistWithTrackRequested += OnCreateNewPlaylistWithTrackRequested;
            albumsViewControl.InfoRequested += OnInfoRequested;
            albumsViewControl.ShowInExplorerRequested += OnShowInExplorerRequested;
            albumsViewControl.ShowInArtistsRequested += OnShowInArtistsRequested;
            albumsViewControl.ShowInAlbumsRequested += OnShowInAlbumsRequested;
            albumsViewControl.ShowInQueueRequested += OnShowInQueueRequested;
            albumsViewControl.ShowInSongsRequested += OnShowInSongsRequested;
            albumsViewControl.RemoveFromLibraryRequested += OnRemoveFromLibraryRequested;
            albumsViewControl.AddMusicFolderRequested += OnAddMusicFolderRequested;
            albumsViewControl.DeleteRequested += OnDeleteRequested;
            albumsViewControl.ArtistNavigationRequested += AlbumsView_ArtistNavigationRequested;
            albumsViewControl.GenreNavigationRequested += AlbumsView_GenreNavigationRequested;
            albumsViewControl.AlbumGridRebuildStatus = (phase, done, total, songs) =>
                Dispatcher.BeginInvoke(
                    () => UpdateStatusBarAlbumGridRebuild(phase, done, total, songs),
                    DispatcherPriority.Normal);

            // Defer creating all other views until the app is idle, or until navigated to
            Dispatcher.InvokeAsync(EnsureOtherViewsCreated, DispatcherPriority.Background);
        }

        private void EnsureOtherViewsCreated()
        {
            if (_otherViewsCreated) return;
            _otherViewsCreated = true;

            songsView = new SongsView();
            queueViewControl = new QueueView();
            recentlyPlayedViewControl = new RecentlyPlayedView();
            artistsViewControl = new ArtistGenreView { ViewName = "Artists" };
            genresViewControl = new ArtistGenreView { ViewName = "Genres" };
            playlistsViewControl = new PlaylistsView();
            playlistsViewControl.LibraryTracks = allTracks;

            void OnPlayTrackRequested(object? s, Song track) => PlayTrack(track, s);
            songsView.PlayTrackRequested += OnPlayTrackRequested;
            queueViewControl.PlayTrackRequested += OnPlayTrackRequested;
            recentlyPlayedViewControl.PlayTrackRequested += OnPlayTrackRequested;
            artistsViewControl.PlayTrackRequested += OnPlayTrackRequested;
            genresViewControl.PlayTrackRequested += OnPlayTrackRequested;
            playlistsViewControl.PlayTrackRequested += OnPlayTrackRequested;

            songsView.PlayNextRequested += OnPlayNextRequested;
            queueViewControl.PlayNextRequested += OnPlayNextRequested;
            recentlyPlayedViewControl.PlayNextRequested += OnPlayNextRequested;
            artistsViewControl.PlayNextRequested += OnPlayNextRequested;
            genresViewControl.PlayNextRequested += OnPlayNextRequested;
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
            playlistsViewControl.AddToQueueRequested += OnAddToQueueRequested;
            playlistsViewControl.AddTrackToPlaylistRequested += OnAddTrackToPlaylistRequested;
            playlistsViewControl.CreateNewPlaylistWithTrackRequested += OnCreateNewPlaylistWithTrackRequested;
            playlistsViewControl.InfoRequested += OnInfoRequested;

            songsView.ShowInExplorerRequested += OnShowInExplorerRequested;
            queueViewControl.ShowInExplorerRequested += OnShowInExplorerRequested;
            recentlyPlayedViewControl.ShowInExplorerRequested += OnShowInExplorerRequested;
            artistsViewControl.ShowInExplorerRequested += OnShowInExplorerRequested;
            genresViewControl.ShowInExplorerRequested += OnShowInExplorerRequested;
            playlistsViewControl.ShowInExplorerRequested += OnShowInExplorerRequested;
            songsView.ShowInArtistsRequested += OnShowInArtistsRequested;
            queueViewControl.ShowInArtistsRequested += OnShowInArtistsRequested;
            recentlyPlayedViewControl.ShowInArtistsRequested += OnShowInArtistsRequested;
            artistsViewControl.ShowInArtistsRequested += OnShowInArtistsRequested;
            genresViewControl.ShowInArtistsRequested += OnShowInArtistsRequested;
            playlistsViewControl.ShowInArtistsRequested += OnShowInArtistsRequested;
            songsView.ShowInAlbumsRequested += OnShowInAlbumsRequested;
            queueViewControl.ShowInAlbumsRequested += OnShowInAlbumsRequested;
            recentlyPlayedViewControl.ShowInAlbumsRequested += OnShowInAlbumsRequested;
            artistsViewControl.ShowInAlbumsRequested += OnShowInAlbumsRequested;
            genresViewControl.ShowInAlbumsRequested += OnShowInAlbumsRequested;
            playlistsViewControl.ShowInAlbumsRequested += OnShowInAlbumsRequested;
            songsView.ShowInQueueRequested += OnShowInQueueRequested;
            recentlyPlayedViewControl.ShowInQueueRequested += OnShowInQueueRequested;
            artistsViewControl.ShowInQueueRequested += OnShowInQueueRequested;
            genresViewControl.ShowInQueueRequested += OnShowInQueueRequested;
            playlistsViewControl.ShowInQueueRequested += OnShowInQueueRequested;
            queueViewControl.ShowInSongsRequested += OnShowInSongsRequested;
            recentlyPlayedViewControl.ShowInSongsRequested += OnShowInSongsRequested;
            artistsViewControl.ShowInSongsRequested += OnShowInSongsRequested;
            genresViewControl.ShowInSongsRequested += OnShowInSongsRequested;
            playlistsViewControl.ShowInSongsRequested += OnShowInSongsRequested;

            songsView.RemoveFromLibraryRequested += OnRemoveFromLibraryRequested;
            queueViewControl.RemoveFromLibraryRequested += OnRemoveFromLibraryRequested;
            recentlyPlayedViewControl.RemoveFromLibraryRequested += OnRemoveFromLibraryRequested;
            artistsViewControl.RemoveFromLibraryRequested += OnRemoveFromLibraryRequested;
            genresViewControl.RemoveFromLibraryRequested += OnRemoveFromLibraryRequested;
            playlistsViewControl.RemoveFromLibraryRequested += OnRemoveFromLibraryRequested;

            songsView.AddMusicFolderRequested += OnAddMusicFolderRequested;
            artistsViewControl.AddMusicFolderRequested += OnAddMusicFolderRequested;
            genresViewControl.AddMusicFolderRequested += OnAddMusicFolderRequested;
            playlistsViewControl.AddMusicFolderRequested += OnAddMusicFolderRequested;

            songsView.DeleteRequested += OnDeleteRequested;
            queueViewControl.DeleteRequested += OnDeleteRequested;
            recentlyPlayedViewControl.DeleteRequested += OnDeleteRequested;
            artistsViewControl.DeleteRequested += OnDeleteRequested;
            genresViewControl.DeleteRequested += OnDeleteRequested;
            playlistsViewControl.DeleteRequested += OnDeleteRequested;

            queueViewControl.TracksReordered += OnQueueTracksReordered;
            queueViewControl.QueueToolbarRemoveRequested += OnQueueToolbarRemoveRequested;
            queueViewControl.QueueToolbarMoveUpRequested += OnQueueToolbarMoveUpRequested;
            queueViewControl.QueueToolbarMoveDownRequested += OnQueueToolbarMoveDownRequested;

            playlistsViewControl.CreatePlaylistRequested += PlaylistsViewControl_CreatePlaylistRequested;
            playlistsViewControl.ImportPlaylistRequested += PlaylistsViewControl_ImportPlaylistRequested;
            playlistsViewControl.ExportPlaylistRequested += PlaylistsViewControl_ExportPlaylistRequested;
            playlistsViewControl.DeletePlaylistRequested += PlaylistsViewControl_DeletePlaylistRequested;
            playlistsViewControl.PlaylistPinnedChanged += PlaylistsViewControl_PlaylistPinnedChanged;
            playlistsViewControl.RemoveFromPlaylistRequested += OnRemoveFromPlaylistRequested;

            ApplyInitialMainView(appSettings.LastActiveView);
            UpdateUI();
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
                var libraryCacheTask = libraryManager.LoadLibraryCacheAsync();
                var recentlyPlayedTask = libraryManager.LoadRecentlyPlayedAsync();
                var playlistsTask = libraryManager.LoadPlaylistsAsync();
                await Task.WhenAll(libraryCacheTask, recentlyPlayedTask, playlistsTask);

                var libraryCache = libraryCacheTask.Result;
                var recentlyPlayedCache = recentlyPlayedTask.Result;
                var playlistsCache = playlistsTask.Result;

                await LoadMusicFromSavedFoldersAsync(libraryCache);

                RestorePlaylists(playlistsCache);

                // Sync pinned playlists for sidebar (so they appear in the menu on launch)
                foreach (var p in playlists)
                    if (p.IsPinned)
                        PinnedPlaylists.Add(p);
                OnPropertyChanged(nameof(HasPinnedPlaylists));

                RestoreRecentlyPlayed(recentlyPlayedCache);

                UpdateUI();

                PrewarmAlbumsGrid();

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
            var libraryFolders = await libraryManager.LoadLibraryFoldersAsync();
            var musicFolders = libraryFolders.MusicFolders;
            if (musicFolders == null || musicFolders.Count == 0)
                return;

            ResetLibraryPathRegistry();

            libraryCache ??= await libraryManager.LoadLibraryCacheAsync();

            var anyFolderDiskScan = false;
            foreach (var folderPath in musicFolders)
            {
                if (Directory.Exists(folderPath))
                {
                    bool hasNewFiles = await libraryManager.HasNewFilesInFolderAsync(folderPath, libraryFolders);

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

                // File.Exists + metadata patching on background thread
                var (validTracks, tracksNeedingThumbnails) = await Task.Run(() =>
                {
                    var valid = new List<Song>(cachedTracks.Count);
                    var needThumb = new List<Song>();

                    foreach (var track in cachedTracks)
                    {
                        if (!File.Exists(track.FilePath))
                            continue;
                        if (!TryRegisterLibraryPath(track.FilePath))
                            continue;

                        if (string.IsNullOrEmpty(track.FileType))
                        {
                            var extension = Path.GetExtension(track.FilePath);
                            if (!string.IsNullOrEmpty(extension))
                                track.FileType = extension.TrimStart('.').ToUpper();
                        }

                        if (track.DurationTimeSpan == TimeSpan.Zero && !string.IsNullOrEmpty(track.Duration))
                        {
                            var parts = track.Duration.Split(':');
                            if (parts.Length == 2 && int.TryParse(parts[0], out int minutes) && int.TryParse(parts[1], out int seconds))
                                track.DurationTimeSpan = new TimeSpan(0, minutes, seconds);
                        }

                        if (string.IsNullOrEmpty(track.Bitrate) && track.FileSize > 0 && track.DurationTimeSpan.TotalSeconds > 0)
                        {
                            var bitrateKbps = (int)((track.FileSize * 8) / (track.DurationTimeSpan.TotalSeconds * 1000));
                            if (bitrateKbps > 0)
                                track.Bitrate = $"{bitrateKbps} kbps";
                        }

                        valid.Add(track);

                        if (string.IsNullOrEmpty(track.ThumbnailCachePath) || !File.Exists(track.ThumbnailCachePath))
                            needThumb.Add(track);
                    }

                    return (valid, needThumb);
                });

                allTracks.AddRange(validTracks);
                filteredTracks.AddRange(validTracks);

                if (tracksNeedingThumbnails.Count > 0)
                {
                    var snapshot = tracksNeedingThumbnails;
                    _ = Task.Run(async () =>
                    {
                        var updates = new List<(Song track, string path)>(snapshot.Count);
                        foreach (var t in snapshot)
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

                UpdateStatusBar();
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
                    var track = allTracks.FirstOrDefault(t =>
                        string.Equals(t.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase));
                    if (track != null)
                    {
                        recentlyPlayed.Add(track);
                    }
                }
            }
        }

        /// <summary>
        /// Binds the albums grid in the background after library load so the grid and its
        /// artwork are already built before the user first navigates to the Albums view.
        /// </summary>
        private void PrewarmAlbumsGrid()
        {
            if (albumsViewControl == null || allTracks.Count == 0)
                return;

            Dispatcher.InvokeAsync(() =>
            {
                if (albumsViewControl != null && !albumsViewControl.IsGridCurrentFor(AlbumsBrowseMode.AllAlbums, allTracks))
                    albumsViewControl.SetBrowseModeAndSource(AlbumsBrowseMode.AllAlbums, allTracks);
            }, DispatcherPriority.Background);
        }

        /// <summary>
        /// Updates the UI after loading data
        /// </summary>
        private void UpdateUI()
        {
            RefreshAllViewDataSources();
            RefreshVisibleViews();
            // Rebuild albums even when not visible so first-run art isn't stuck after scan.
            albumsViewControl?.RefreshAlbumGridFromLibrary();
        }

        private void StartThumbnailCacheBackfillIfNeeded()
        {
            var snapshot = allTracks
                .Where(t => string.IsNullOrEmpty(t.ThumbnailCachePath) || !File.Exists(t.ThumbnailCachePath))
                .ToList();
            if (snapshot.Count == 0)
                return;

            _ = Task.Run(async () =>
            {
                var updates = new List<(Song track, string path)>(snapshot.Count);
                foreach (var t in snapshot)
                {
                    var path = AlbumArtCacheManager.GenerateAndCache(t);
                    if (!string.IsNullOrEmpty(path))
                        updates.Add((t, path));
                }

                await Dispatcher.InvokeAsync(async () =>
                {
                    foreach (var (track, path) in updates)
                        track.ThumbnailCachePath = path;
                    await UpdateLibraryCacheAsync();
                    RefreshTitleBarFromCurrentTrack();
                });
            });
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

        private void UpdateStatusBarAlbumGridRebuild(AlbumsGridRebuildPhase phase, int done, int total, int songCount)
        {
            if (statusBarText == null)
                return;

            switch (phase)
            {
                case AlbumsGridRebuildPhase.Grouping:
                    if (progressBarFill != null)
                    {
                        progressBarFill.Visibility = Visibility.Collapsed;
                        progressBarFill.Width = 0;
                    }
                    statusBarText.Text = $"{songCount} songs, grouping albums…";
                    break;
                case AlbumsGridRebuildPhase.LoadingArtwork:
                    if (total <= 0)
                        statusBarText.Text = $"{songCount} songs, loading artwork…";
                    else
                        statusBarText.Text = $"{songCount} songs, loading artwork {done}/{total}…";
                    if (progressBarBackground != null && progressBarFill != null && total > 0)
                    {
                        progressBarFill.Visibility = Visibility.Visible;
                        var w = progressBarBackground.ActualWidth;
                        if (w > 0)
                            progressBarFill.Width = w * (done / (double)total);
                    }
                    break;
                case AlbumsGridRebuildPhase.Complete:
                    if (progressBarFill != null)
                    {
                        progressBarFill.Visibility = Visibility.Collapsed;
                        progressBarFill.Width = 0;
                    }
                    UpdateStatusBar();
                    break;
            }
        }

        private async Task SortLibraryTracksByPathForScanAsync()
        {
            if (allTracks.Count <= 1)
                return;

            var snapshot = allTracks.ToList();
            var sorted = await Task.Run(() => snapshot
                .OrderBy(t => t.FilePath ?? "", StringComparer.OrdinalIgnoreCase)
                .ToList());

            const int yieldEvery = 500;
            allTracks.Clear();
            filteredTracks.Clear();
            for (int i = 0; i < sorted.Count; i++)
            {
                allTracks.Add(sorted[i]);
                filteredTracks.Add(sorted[i]);
                if ((i + 1) % yieldEvery == 0)
                    await Dispatcher.Yield(DispatcherPriority.Background);
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

            // Always refresh albums after batch edits — navigating back with IsGridCurrentFor
            // would otherwise keep first-run placeholder tiles.
            if (albumsViewControl != null)
            {
                var albumsIsContent = contentHost != null && ReferenceEquals(contentHost.Content, albumsViewControl);
                if (!(albumsIsContent && allowInPlaceAlbumPatch && updatedTrack != null &&
                      albumsViewControl.TryRefreshAlbumGroupInPlace(updatedTrack)))
                    albumsViewControl.RefreshAlbumGridFromLibrary();
            }

            playlistsViewControl?.RefreshTrackListBindings();
        }

        private int GetTitleBarAlbumArtTargetPixelSize()
        {
            try
            {
                if (titleBarPlayer != null)
                    return AlbumArtLoader.GetTitleBarTargetPixelSize(VisualTreeHelper.GetDpi(titleBarPlayer));
            }
            catch
            {
                // ignore
            }

            return (int)Math.Ceiling(UILayoutConstants.TitleBarAlbumArtLogicalSizeDip);
        }

        private void RefreshTitleBarFromCurrentTrack()
        {
            if (currentTrack == null)
                return;

            var albumArt = AlbumArtLoader.LoadAlbumArt(currentTrack, GetTitleBarAlbumArtTargetPixelSize());
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
            ShowAlbumsView(bindFullLibrary: false);
            if (albumsViewControl != null && album.Songs.Count > 0)
                albumsViewControl.SetBrowseModeAndSource(AlbumsBrowseMode.AllAlbums, album.Songs);
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

                if (shuffledTracks.Count > 1)
                    ShuffleRangeUntilOrderDiffersFromLinear(shuffledTracks, filteredTracks, 0, shuffledTracks.Count - 1);

                if (currentTrack != null)
                {
                    currentTrackIndex = filteredTracks.IndexOf(currentTrack);
                    currentShuffledIndex = FindTrackIndexInPlayQueue(shuffledTracks, currentTrack);
                    if (currentShuffledIndex < 0)
                        currentShuffledIndex = 0;
                }
                else
                {
                    currentShuffledIndex = -1;
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

        /// <summary>
        /// Keeps library shuffle order stable when <see cref="filteredTracks"/> membership changes:
        /// drop removed tracks, append newly added ones into the unplayed tail (after the playhead).
        /// Full reshuffle only if the list was empty or the current track is missing.
        /// </summary>
        private void ReconcileLibraryShuffledTracks()
        {
            if (!titleBarPlayer.IsShuffleEnabled || HasContextualPlaybackQueue())
                return;

            if (filteredTracks == null || filteredTracks.Count == 0)
            {
                shuffledTracks.Clear();
                currentShuffledIndex = -1;
                return;
            }

            if (shuffledTracks.Count == 0)
            {
                RegenerateShuffledTracks();
                return;
            }

            var filteredSet = new HashSet<Song>();
            foreach (var t in filteredTracks)
            {
                if (t != null && !string.IsNullOrEmpty(t.FilePath))
                    filteredSet.Add(t);
            }

            for (int i = shuffledTracks.Count - 1; i >= 0; i--)
            {
                var t = shuffledTracks[i];
                if (t == null || !filteredSet.Contains(t))
                    shuffledTracks.RemoveAt(i);
            }

            var shuffledSet = new HashSet<Song>(shuffledTracks);
            var newcomers = new List<Song>();
            foreach (var t in filteredTracks)
            {
                if (t != null && !string.IsNullOrEmpty(t.FilePath) && !shuffledSet.Contains(t))
                    newcomers.Add(t);
            }

            if (newcomers.Count > 1)
                FisherYatesRange(newcomers, 0, newcomers.Count - 1);

            int insertAt = currentShuffledIndex >= 0 ? currentShuffledIndex + 1 : shuffledTracks.Count;
            if (insertAt > shuffledTracks.Count)
                insertAt = shuffledTracks.Count;

            foreach (var t in newcomers)
            {
                shuffledTracks.Insert(insertAt, t);
                insertAt++;
            }

            if (currentTrack != null)
            {
                currentTrackIndex = filteredTracks.IndexOf(currentTrack);
                currentShuffledIndex = FindTrackIndexInPlayQueue(shuffledTracks, currentTrack);
                if (currentShuffledIndex < 0)
                    RegenerateShuffledTracks();
            }
            else
            {
                currentShuffledIndex = -1;
            }
        }

        private void ValidateAndSyncLibraryShuffleIndices(Song track)
        {
            if (track == null || filteredTracks == null)
                return;

            if (shuffledTracks.Count != filteredTracks.Count)
            {
                ReconcileLibraryShuffledTracks();
                if (shuffledTracks.Count != filteredTracks.Count)
                    RegenerateShuffledTracks();
                else
                {
                    int siAfter = FindTrackIndexInPlayQueue(shuffledTracks, track);
                    if (siAfter >= 0)
                        currentShuffledIndex = siAfter;
                    currentTrackIndex = filteredTracks.IndexOf(track);
                }
                return;
            }

            int li = filteredTracks.IndexOf(track);
            if (li < 0)
            {
                RegenerateShuffledTracks();
                return;
            }
            currentTrackIndex = li;

            int si = FindTrackIndexInPlayQueue(shuffledTracks, track);
            if (si < 0)
            {
                RegenerateShuffledTracks();
                return;
            }
            currentShuffledIndex = si;
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
                    if (shuffledTracks.Count == 0)
                        RegenerateShuffledTracks();
                    else
                        ReconcileLibraryShuffledTracks();
                    return;
                }

                if (currentTrack != null && FindTrackIndexInPlayQueue(shuffledTracks, currentTrack) == -1)
                {
                    RegenerateShuffledTracks();
                    return;
                }

                if (currentTrack != null && currentShuffledIndex == -1)
                {
                    currentShuffledIndex = FindTrackIndexInPlayQueue(shuffledTracks, currentTrack);
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
                if (shuffledTracks.Count == 0)
                    RegenerateShuffledTracks();
                else if (shuffledTracks.Count != filteredTracks.Count)
                    ReconcileLibraryShuffledTracks();
                else
                    EnsureShuffledTracksInitialized();
            }
        }

        private void UpdateShuffleIndicesAfterTrackChange(Song track)
        {
            if (!titleBarPlayer.IsShuffleEnabled || HasContextualPlaybackQueue())
                return;

            ValidateAndSyncLibraryShuffleIndices(track);
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
            if (_useSoftwareSessionVolume && _sessionVolumeProvider != null)
            {
                if (titleBarPlayer.IsMuted)
                    _sessionVolumeProvider.Volume = 0f;
                else
                    _sessionVolumeProvider.Volume = (float)(volume0To100 / 100.0);
            }

            _pendingVolume0To100 = volume0To100;
            if (_volumeSaveTimer == null)
            {
                _volumeSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                _volumeSaveTimer.Tick += async (_, _) =>
                {
                    _volumeSaveTimer.Stop();
                    try
                    {
                        await SettingsManager.Instance.SetTitleBarVolume0To100Async(_pendingVolume0To100);
                    }
                    catch { }
                };
            }
            _volumeSaveTimer.Stop();
            _volumeSaveTimer.Start();
        }

        internal void HandlePlayPauseHotkey()
        {
            TitleBarPlayer_PlayPauseRequested(this, EventArgs.Empty);
        }

        internal void HandlePreviousTrackHotkey()
        {
            TitleBarPlayer_PreviousTrackRequested(this, EventArgs.Empty);
            SelectCurrentTrackInActiveView(true);
        }

        internal void HandleNextTrackHotkey()
        {
            TitleBarPlayer_NextTrackRequested(this, EventArgs.Empty);
            SelectCurrentTrackInActiveView(true);
        }

        internal void HandlePlaySelectedTrackHotkey()
        {
            if (!TryResolveTrackToPlayFromActiveView(out var selected, out var requestSource))
                return;

            if (selected != null)
                PlayTrack(selected, requestSource);
        }

        private bool TryResolveTrackToPlayFromActiveView(out Song? track, out object? requestSource)
        {
            track = null;
            requestSource = null;
            var current = contentHost?.Content;

            if (ReferenceEquals(current, songsView))
            {
                requestSource = songsView;
                track = songsView?.SelectedTrack
                    ?? filteredTracks.FirstOrDefault()
                    ?? allTracks.FirstOrDefault();
                return track != null;
            }

            if (ReferenceEquals(current, queueViewControl))
            {
                requestSource = queueViewControl;
                track = queueViewControl?.SelectedTrack
                    ?? GetCurrentPlayQueue()?.FirstOrDefault();
                return track != null;
            }

            if (ReferenceEquals(current, artistsViewControl))
            {
                requestSource = artistsViewControl;
                track = artistsViewControl?.SelectedTrack;
                return track != null;
            }

            if (ReferenceEquals(current, genresViewControl))
            {
                requestSource = genresViewControl;
                track = genresViewControl?.SelectedTrack;
                return track != null;
            }

            if (ReferenceEquals(current, albumsViewControl))
            {
                requestSource = albumsViewControl;
                track = albumsViewControl?.GetDefaultPlayTrack();
                return track != null;
            }

            if (ReferenceEquals(current, playlistsViewControl))
            {
                requestSource = playlistsViewControl;
                track = playlistsViewControl?.SelectedTrack
                    ?? playlistsViewControl?.SelectedPlaylist?.Tracks?.FirstOrDefault();
                return track != null;
            }

            if (ReferenceEquals(current, recentlyPlayedViewControl))
            {
                requestSource = recentlyPlayedViewControl;
                track = recentlyPlayedViewControl?.SelectedTrack
                    ?? recentlyPlayed.FirstOrDefault();
                return track != null;
            }

            return false;
        }

        private void SelectCurrentTrackInActiveView(bool grabFocus = false)
        {
            var track = currentTrack;
            if (track == null) return;
            var current = contentHost?.Content;

            if (ReferenceEquals(current, songsView))
                songsView?.SelectTrack(track, grabFocus);
            else if (ReferenceEquals(current, queueViewControl))
                queueViewControl?.SelectTrack(track, grabFocus);
            else if (ReferenceEquals(current, artistsViewControl))
                artistsViewControl?.SelectTrack(track, grabFocus);
            else if (ReferenceEquals(current, genresViewControl))
                genresViewControl?.SelectTrack(track, grabFocus);
            else if (ReferenceEquals(current, albumsViewControl))
                albumsViewControl?.SelectTrack(track, grabFocus);
            else if (ReferenceEquals(current, playlistsViewControl))
                playlistsViewControl?.SelectTrack(track, grabFocus);
            else if (ReferenceEquals(current, recentlyPlayedViewControl))
                recentlyPlayedViewControl?.SelectTrack(track, grabFocus);
        }

        private void TitleBarPlayer_PlayPauseRequested(object? sender, EventArgs e)
        {
            if (currentTrack == null)
            {
                if (TryResolveTrackToPlayFromActiveView(out var selected, out var requestSource) && selected != null)
                {
                    PlayTrack(selected, requestSource);
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
            isManualNavigation = true;

            // Title bar shows the incoming track during overlap — treat that as current
            TryAdoptVisibleCrossfadeTrackAsCurrent();

            var currentQueue = GetCurrentPlayQueue();
            var currentIndex = GetCurrentTrackIndex();

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
            ClearContextualPlaybackQueue();
        }

        private void TitleBarPlayer_NextTrackRequested(object? sender, EventArgs e)
        {
            isManualNavigation = true;

            // Title bar shows the incoming track during overlap — skip that, not the fade-out
            TryAdoptVisibleCrossfadeTrackAsCurrent();

            var currentQueue = GetCurrentPlayQueue();
            var currentIndex = GetCurrentTrackIndex();
            var repeatMode = titleBarPlayer.RepeatMode;

            if (HasContextualPlaybackQueue())
            {
                if (TryAdvanceContextualSessionMovingFinishedToHistory(out var next) && next != null)
                {
                    bool wasPlaying = titleBarPlayer.IsPlaying;
                    LoadTrackWithoutPlayback(next);
                    if (wasPlaying)
                        ResumePlayback();
                }
                else if (repeatMode == SettingsManager.RepeatMode.All &&
                         TryWrapContextualForRepeatAll(out var wrapStart) &&
                         wrapStart != null)
                {
                    bool wasPlaying = titleBarPlayer.IsPlaying;
                    LoadTrackWithoutPlayback(wrapStart);
                    if (wasPlaying)
                        ResumePlayback();
                }
                else
                {
                    ResetPlaybackToIdleAndRefreshQueue();
                }

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
            else if (repeatMode == SettingsManager.RepeatMode.All &&
                     TryWrapLibraryForRepeatAll(out var libStart) &&
                     libStart != null)
            {
                bool wasPlaying = titleBarPlayer.IsPlaying;
                LoadTrackWithoutPlayback(libStart);
                if (wasPlaying)
                    ResumePlayback();
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
                if (isShuffleEnabled)
                {
                    BuildShuffledFutureForAnchor(currentTrack);
                    CaptureContextualShuffleWrapPathOrder();
                }
                else
                {
                    contextualShuffleWrapPathOrder = null;
                    contextualShuffledFuture.Clear();
                }

                SetActivePlaybackFuture(currentTrack);
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

            UpdateQueueView();
            RefreshVisibleViews();
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

            var session = GetOrPromoteContextualSessionForQueueEdit();
            if (session == null)
                return;

            bool sameAsCurrent = currentTrack != null && SameSongPath(currentTrack, track);

            if (!sameAsCurrent)
            {
                RemoveFromSessionOrderedFullSkippingCurrent(track);
                RemoveFromShuffledFutureSkippingHead(track);

                int curIdx = currentTrack != null
                    ? Helpers.ArtistPlaybackOrder.IndexOfTrackInOrderedList(session, currentTrack)
                    : -1;
                int insertIdx = curIdx + 1;
                if (insertIdx < 0 || insertIdx > session.Count)
                    insertIdx = session.Count;
                session.Insert(insertIdx, track);

                if (titleBarPlayer.IsShuffleEnabled)
                {
                    int shInsert = contextualShuffledFuture.Count >= 1 ? 1 : 0;
                    contextualShuffledFuture.Insert(shInsert, track);
                    CaptureContextualShuffleWrapPathOrder();
                }
            }

            MarkUserQueued(track);
            SetActivePlaybackFuture(currentTrack);
            UpdateQueueView();
            RefreshVisibleViews();
        }

        /// <summary>
        /// Appends the given track to the end of the current queue.
        /// </summary>
        private void OnAddToQueueRequested(object? sender, Song track)
        {
            if (!IsValidTrackWithPath(track))
                return;

            var session = GetOrPromoteContextualSessionForQueueEdit();
            if (session == null)
                return;

            bool sameAsCurrent = currentTrack != null && SameSongPath(currentTrack, track);

            if (!sameAsCurrent)
            {
                RemoveFromSessionOrderedFullSkippingCurrent(track);
                RemoveFromShuffledFutureSkippingHead(track);

                session.Add(track);
                if (titleBarPlayer.IsShuffleEnabled)
                {
                    contextualShuffledFuture.Add(track);
                    CaptureContextualShuffleWrapPathOrder();
                }
            }

            MarkUserQueued(track);
            SetActivePlaybackFuture(currentTrack);
            UpdateQueueView();
            RefreshVisibleViews();
        }

        private void MarkUserQueued(Song track)
        {
            if (track == null) return;
            track.IsUserQueued = true;
            userQueuedSongs.Add(track);
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
                SameSongPath(removed, currentTrack);

            bool wasPlaying = titleBarPlayer.IsPlaying;

            if (HasContextualPlaybackQueue() && contextualSessionOrderedFull != null && removed != null)
            {
                if (titleBarPlayer.IsShuffleEnabled && q >= 0 && q < contextualShuffledFuture.Count)
                    contextualShuffledFuture.RemoveAt(q);
                else
                {
                    int sIdx = contextualShuffledFuture.FindIndex(t => ReferenceEquals(t, removed));
                    if (sIdx < 0)
                        sIdx = IndexOfBySongPath(contextualShuffledFuture, removed);
                    if (sIdx >= 0)
                        contextualShuffledFuture.RemoveAt(sIdx);
                }

                if (titleBarPlayer.IsShuffleEnabled)
                    CaptureContextualShuffleWrapPathOrder();

                int sessionIdx = contextualSessionOrderedFull.FindIndex(t => ReferenceEquals(t, removed));
                if (sessionIdx < 0)
                    sessionIdx = IndexOfBySongPath(contextualSessionOrderedFull, removed);
                if (sessionIdx >= 0)
                    contextualSessionOrderedFull.RemoveAt(sessionIdx);

                ClearInjectedFlagFor(removed);

                Song? anchor = removeWasCurrent
                    ? (titleBarPlayer.IsShuffleEnabled
                        ? (contextualShuffledFuture.Count > 0 ? contextualShuffledFuture[0] : null)
                        : FindNaturalNextAfter(removed))
                    : currentTrack;

                SetActivePlaybackFuture(anchor);
            }
            else
            {
                queue.RemoveAt(q);
                ClearInjectedFlagFor(removed);
            }

            if (removeWasCurrent)
            {
                var nextQueue = GetCurrentPlayQueue();
                int nextBase = GetCurrentTrackIndex();
                if (nextQueue != null && nextBase >= 0 && nextBase < nextQueue.Count)
                {
                    var next = nextQueue[nextBase];
                    if (next != null)
                    {
                        if (wasPlaying)
                            PlayTrack(next, null);
                        else
                            LoadTrackWithoutPlayback(next);
                    }
                    else
                        StopPlayback();
                }
                else if (nextQueue != null && nextQueue.Count > 0)
                {
                    var next = nextQueue[0];
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
        /// Removes the track(s) from the musicApp library (in-memory and persisted). Does not delete files.
        /// </summary>
        private async void OnRemoveFromLibraryRequested(object? sender, IReadOnlyList<Song> tracks)
        {
            if (tracks == null || tracks.Count == 0)
                return;
            var distinct = tracks
                .Where(IsValidTrackWithPath)
                .GroupBy(t =>
                {
                    var n = LibraryPathHelper.TryNormalizePath(t.FilePath);
                    return string.IsNullOrEmpty(n) ? t.FilePath : n;
                }, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            if (distinct.Count == 0)
                return;

            string message = distinct.Count == 1
                ? $"Remove \"{distinct[0].Title}\" from the library? The file will stay on your computer."
                : $"Remove {distinct.Count} tracks from the library? The files will stay on your computer.";
            var result = MessageDialog.Show(this, "Remove from Library", message, MessageDialog.Buttons.YesNo);
            if (result != true)
                return;
            await RemoveTracksFromLibraryAsync(distinct);
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
        private Task RemoveTrackFromLibraryAsync(Song track)
        {
            if (track == null)
                return Task.CompletedTask;
            return RemoveTracksFromLibraryAsync(new List<Song> { track });
        }

        private async Task RemoveTracksFromLibraryAsync(IReadOnlyList<Song> tracks)
        {
            if (tracks == null || tracks.Count == 0)
                return;

            var pathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in tracks)
            {
                if (t?.FilePath != null)
                    pathSet.Add(t.FilePath);
            }

            if (currentTrack != null && pathSet.Contains(currentTrack.FilePath))
            {
                CleanupAudioObjects();
                currentTrack = null;
                currentTrackIndex = -1;
                currentShuffledIndex = -1;
                ClearContextualPlaybackQueue();
                titleBarPlayer.SetTrackInfo("No track selected", "", "");
            }

            foreach (var track in tracks)
            {
                if (track != null)
                    RemoveTrackFromCollections(track, includeShuffled: true);
            }

            foreach (var playlist in playlists)
            {
                var toRemove = playlist.Tracks.Where(t => pathSet.Contains(t.FilePath)).ToList();
                foreach (var t in toRemove)
                    playlist.RemoveTrack(t);
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

                int processedCount = 0;
                int scanConcurrencySmoothed = 0;
                const int scanBatchSize = 320;

                var pendingScanTracks = new List<Song>();
                var pendingScanLock = new object();
                SystemResourceSnapshot? lastResourceSnapshot = null;
                var batchesSinceSample = int.MaxValue;
                var batchIndex = 0;

                void DrainPendingToUi()
                {
                    List<Song>? toAdd = null;
                    lock (pendingScanLock)
                    {
                        if (pendingScanTracks.Count > 0)
                        {
                            toAdd = pendingScanTracks;
                            pendingScanTracks = new List<Song>();
                        }
                    }

                    if (toAdd != null)
                    {
                        foreach (var t in toAdd)
                        {
                            allTracks.Add(t);
                            filteredTracks.Add(t);
                        }
                    }

                    var done = Volatile.Read(ref processedCount);
                    if (musicFiles.Count > 0)
                    {
                        double progressPercent = done / (double)musicFiles.Count;
                        progressBarFill.Width = progressBarBackground.ActualWidth * progressPercent;
                    }

                    if (progressBarFill.Visibility == Visibility.Visible)
                        UpdateStatusBarScanningLight(done, musicFiles.Count);
                }

                var uiPublishTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(200)
                };
                uiPublishTimer.Tick += (_, _) => DrainPendingToUi();
                uiPublishTimer.Start();

                try
                {
                    foreach (var batch in musicFiles.Chunk(scanBatchSize))
                    {
                        var mustSample = batchIndex == 0 || batchesSinceSample >= 4;

                        int dop;
                        if (mustSample)
                        {
                            var sampleInterval = TimeSpan.FromMilliseconds(50);
                            lastResourceSnapshot = await Task.Run(() => WindowsSystemMetrics.Sample(sampleInterval));
                            dop = ScanConcurrencyAdvisor.Recommend(
                                lastResourceSnapshot.Value,
                                Environment.ProcessorCount,
                                ref scanConcurrencySmoothed);
                            batchesSinceSample = 0;
                        }
                        else
                        {
                            dop = scanConcurrencySmoothed;
                        }

                        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = dop };

                        await Task.Run(() => Parallel.ForEach(batch, parallelOptions, file =>
                        {
                            Song? track = null;
                            try
                            {
                                if (TryRegisterLibraryPath(file))
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

                            Interlocked.Increment(ref processedCount);
                            if (track != null)
                            {
                                lock (pendingScanLock)
                                    pendingScanTracks.Add(track);
                            }
                        }));

                        if (!mustSample)
                            batchesSinceSample++;
                        batchIndex++;
                    }
                }
                finally
                {
                    uiPublishTimer.Stop();
                }

                DrainPendingToUi();

                await SortLibraryTracksByPathForScanAsync();

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
                            StartThumbnailCacheBackfillIfNeeded();
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
                ClearInjectedFlagFor(track);

                // Set the current track index in the active queue context.
                SyncCurrentTrackIndices(track, requestSource);
                UpdateShuffleIndicesAfterTrackChange(track);

                var albumArt = AlbumArtLoader.LoadAlbumArt(track, GetTitleBarAlbumArtTargetPixelSize());

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
                ClearInjectedFlagFor(track);

                // Set the current track index in the active queue context.
                SyncCurrentTrackIndices(track);
                UpdateShuffleIndicesAfterTrackChange(track);

                var albumArt = AlbumArtLoader.LoadAlbumArt(track, GetTitleBarAlbumArtTargetPixelSize());

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
                SelectCurrentTrackInActiveView(false);
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

        /// <summary>
        /// UI/indices idle while there's no decoded track; does not clear contextual playback (
        /// <see cref="MainWindow.ClearContextualPlaybackQueue"/>).
        /// </summary>
        private void ResetToIdleState()
        {
            try
            {
                currentTrack = null;
                currentTrackIndex = -1;
                currentShuffledIndex = -1;

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