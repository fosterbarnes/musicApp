using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using musicApp.Dialogs;
using musicApp.Helpers;
using musicApp.Views;

namespace musicApp
{
    public partial class MainWindow
    {
        private void BtnLibrary_Click(object sender, RoutedEventArgs e)
        {
            ShowLibraryView();
        }

        private void BtnQueue_Click(object sender, RoutedEventArgs e)
        {
            ShowQueueView();
        }

        private void BtnPlaylists_Click(object sender, RoutedEventArgs e)
        {
            ShowPlaylistsView();
        }

        private void BtnRecentlyPlayed_Click(object sender, RoutedEventArgs e)
        {
            ShowRecentlyPlayedView();
        }

        private void BtnRecentlyAdded_Click(object sender, RoutedEventArgs e)
        {
            ShowRecentlyAddedView();
        }

        private void BtnArtists_Click(object sender, RoutedEventArgs e)
        {
            ShowArtistsView();
        }

        private void BtnAlbums_Click(object sender, RoutedEventArgs e)
        {
            ShowAlbumsView();
        }

        private void BtnGenres_Click(object sender, RoutedEventArgs e)
        {
            ShowGenresView();
        }

        private async void BtnAddFolder_Click(object sender, RoutedEventArgs e)
        {
            await AddMusicFolderAsync();
        }

        private async void OnAddMusicFolderRequested(object? sender, EventArgs e)
        {
            await AddMusicFolderAsync();
        }

        private async void BtnRescanLibrary_Click(object sender, RoutedEventArgs e)
        {
            await RescanLibraryAsync();
        }

        private async void BtnRemoveFolder_Click(object sender, RoutedEventArgs e)
        {
            await RemoveMusicFolderAsync();
        }

        private void BtnClearSettings_Click(object sender, RoutedEventArgs e)
        {
            ClearSettings();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e) => ShowSettingsWindow();

        private void BtnMiniPlayer_Click(object sender, RoutedEventArgs e) => ShowMiniPlayerWindow();

        private void ShowSettingsWindow(string? launchSection = null)
        {
            if (_settingsWindow != null && _settingsWindow.IsVisible)
            {
                _settingsWindow.Activate();
                return;
            }

            var w = new SettingsView(launchSection) { Owner = this };
            _settingsWindow = w;
            w.Closed += (_, _) =>
            {
                if (ReferenceEquals(_settingsWindow, w))
                    _settingsWindow = null;
            };
            w.Show();
        }

        private void ShowMiniPlayerWindow()
        {
            if (_miniPlayerWindow != null && _miniPlayerWindow.IsVisible)
            {
                _miniPlayerWindow.Activate();
                return;
            }

            var w = new MiniPlayerWindow { Owner = this };
            RestoreMiniPlayerWindowState(w, appSettings.MiniPlayerWindow);
            _miniPlayerWindow = w;
            w.PlayPauseRequested += TitleBarPlayer_PlayPauseRequested;
            w.PreviousTrackRequested += TitleBarPlayer_PreviousTrackRequested;
            w.NextTrackRequested += TitleBarPlayer_NextTrackRequested;
            w.QueueUndoRequested += (_, _) => TryRestorePreviousQueue();
            w.PlaybackPositionCommitted += OnTitleBarPlaybackPositionCommitted;
            w.SongPlayRequested += MiniPlayer_SongPlayRequested;
            w.QueueRemoveRequested += OnQueueToolbarRemoveRequested;
            w.TracksReordered += OnQueueTracksReordered;
            w.PlayNextRequested += OnPlayNextRequested;
            w.AddToQueueRequested += OnAddToQueueRequested;
            w.AddTrackToPlaylistRequested += OnAddTrackToPlaylistRequested;
            w.CreateNewPlaylistWithTrackRequested += OnCreateNewPlaylistWithTrackRequested;
            w.InfoRequested += OnInfoRequested;
            w.ShowInArtistsRequested += OnShowInArtistsRequested;
            w.ShowInSongsRequested += OnShowInSongsRequested;
            w.ShowInAlbumsRequested += OnShowInAlbumsRequested;
            w.ShowInQueueRequested += OnShowInQueueRequested;
            w.ShowInExplorerRequested += OnShowInExplorerRequested;
            w.RemoveFromLibraryRequested += OnRemoveFromLibraryRequested;
            w.DeleteRequested += OnDeleteRequested;
            w.ArtistNavigationRequested += TitleBarPlayer_ArtistNavigationRequested;
            w.AlbumNavigationRequested += TitleBarPlayer_AlbumNavigationRequested;
            w.Closed += MiniPlayerWindow_Closed;
            w.Show();
            PushMiniPlayerState(w);
        }

        private void RestoreMiniPlayerWindowState(MiniPlayerWindow window, SettingsManager.MiniPlayerWindowStateSettings state)
        {
            if (state == null || !state.Left.HasValue || !state.Top.HasValue ||
                !double.IsFinite(state.Width) || !double.IsFinite(state.Height) ||
                !double.IsFinite(state.Left.Value) || !double.IsFinite(state.Top.Value))
                return;

            var workArea = GetMiniPlayerWorkArea(state.Left.Value, state.Top.Value);
            double width = Math.Clamp(state.Width, window.MinWidth, workArea.Width);
            double height = Math.Clamp(state.Height, window.MinHeight, workArea.Height);
            double left = Math.Clamp(state.Left.Value, workArea.Left, workArea.Right - width);
            double top = Math.Clamp(state.Top.Value, workArea.Top, workArea.Bottom - height);

            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Width = width;
            window.Height = height;
            window.Left = left;
            window.Top = top;
        }

        private static Rect GetMiniPlayerWorkArea(double left, double top)
        {
            var monitor = MonitorFromPoint(new POINT((int)Math.Round(left), (int)Math.Round(top)), MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero)
            {
                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    return new Rect(
                        info.rcWork.Left,
                        info.rcWork.Top,
                        info.rcWork.Right - info.rcWork.Left,
                        info.rcWork.Bottom - info.rcWork.Top);
                }
            }

            return SystemParameters.WorkArea;
        }

        private const uint MonitorDefaultToNearest = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct POINT(int x, int y)
        {
            public readonly int X = x;
            public readonly int Y = y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        private async void MiniPlayerWindow_Closed(object? sender, EventArgs e)
        {
            if (sender is not MiniPlayerWindow window)
                return;

            if (!ReferenceEquals(_miniPlayerWindow, window))
                return;

            CaptureMiniPlayerWindowState(window);
            _miniPlayerWindow = null;
            await settingsManager.SaveSettingsAsync(appSettings);
        }

        private void CaptureMiniPlayerWindowState(MiniPlayerWindow window)
        {
            if (window.WindowState != WindowState.Normal ||
                !double.IsFinite(window.Width) || !double.IsFinite(window.Height) ||
                !double.IsFinite(window.Left) || !double.IsFinite(window.Top))
                return;

            appSettings.MiniPlayerWindow = new SettingsManager.MiniPlayerWindowStateSettings
            {
                Width = window.Width,
                Height = window.Height,
                Left = window.Left,
                Top = window.Top
            };
        }

        private void MiniPlayer_SongPlayRequested(object? sender, Song song)
        {
            if (song != null)
                PlayTrack(song, _miniPlayerWindow);
        }

        private void ShowLibraryView()
        {
            EnsureOtherViewsCreated();
            contentHost.Content = songsView;
            SetSidebarNavActive(btnLibrary);
        }

        private void ShowQueueView()
        {
            EnsureOtherViewsCreated();
            contentHost.Content = queueViewControl;
            UpdateQueueView();
            SetSidebarNavActive(btnQueue);
        }

        private void ShowPlaylistsView(Playlist? selectPlaylist = null)
        {
            EnsureOtherViewsCreated();
            contentHost.Content = playlistsViewControl;
            if (playlistsViewControl != null)
            {
                playlistsViewControl.Playlists = playlists;
                playlistsViewControl.SelectPlaylist(selectPlaylist);
            }
            SetSidebarNavActive(btnPlaylists);
        }

        private void PinnedPlaylistSidebar_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is Playlist playlist)
                ShowPlaylistsView(playlist);
        }

        private void ShowRecentlyPlayedView()
        {
            EnsureOtherViewsCreated();
            contentHost.Content = recentlyPlayedViewControl;
            SetSidebarNavActive(btnRecentlyPlayed);
        }

        private void ShowArtistsView()
        {
            EnsureOtherViewsCreated();
            contentHost.Content = artistsViewControl;
            SetSidebarNavActive(btnArtists);
        }

        /// <param name="bindFullLibrary">False when caller assigns a narrower ItemsSource (e.g. search subset).</param>
        private void ShowAlbumsView(bool bindFullLibrary = true)
        {
            if (albumsViewControl != null)
            {
                if (bindFullLibrary && !albumsViewControl.IsGridCurrentFor(AlbumsBrowseMode.AllAlbums, allTracks))
                    albumsViewControl.SetBrowseModeAndSource(AlbumsBrowseMode.AllAlbums, allTracks);
                else if (!bindFullLibrary)
                    albumsViewControl.BrowseMode = AlbumsBrowseMode.AllAlbums;
            }
            contentHost.Content = albumsViewControl;
            SetSidebarNavActive(btnAlbums);
        }

        private void ShowRecentlyAddedView()
        {
            if (albumsViewControl == null)
                return;
            if (!albumsViewControl.IsGridCurrentFor(AlbumsBrowseMode.RecentlyAdded, allTracks))
                albumsViewControl.SetBrowseModeAndSource(AlbumsBrowseMode.RecentlyAdded, allTracks);
            contentHost.Content = albumsViewControl;
            SetSidebarNavActive(btnRecentlyAdded);
        }

        private void ShowGenresView()
        {
            EnsureOtherViewsCreated();
            contentHost.Content = genresViewControl;
            SetSidebarNavActive(btnGenres);
        }

        private void SetSidebarNavActive(Button? activeButton)
        {
            foreach (var b in new[]
                     {
                         btnArtists, btnAlbums, btnLibrary, btnGenres, btnPlaylists, btnRecentlyAdded, btnRecentlyPlayed, btnQueue
                     })
                SidebarNav.SetIsActive(b, false);
            if (activeButton != null)
                SidebarNav.SetIsActive(activeButton, true);
        }

        private void CloseQueuePopupIfFromQueuePopout(object? sender)
        {
            if (queuePopupView != null && ReferenceEquals(sender, queuePopupView))
                CloseQueuePopupProgrammatically();
        }

        private void OnShowInExplorerRequested(object? sender, Song track)
        {
            CloseQueuePopupIfFromQueuePopout(sender);
            if (!IsValidTrackWithPath(track))
                return;
            if (!File.Exists(track.FilePath))
                return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{track.FilePath}\""
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Show in Explorer failed: {ex.Message}");
            }
        }

        private void OnShowInArtistsRequested(object? sender, Song track)
        {
            CloseQueuePopupIfFromQueuePopout(sender);
            if (track == null || string.IsNullOrWhiteSpace(track.Artist))
                return;

            ShowArtistsView();
            artistsViewControl?.SelectTrack(track);
        }

        private void OnShowInAlbumsRequested(object? sender, Song track)
        {
            CloseQueuePopupIfFromQueuePopout(sender);
            if (track == null || string.IsNullOrWhiteSpace(track.Album))
                return;

            ShowAlbumsView();
            albumsViewControl?.SelectAlbum(track);
        }

        private void OnShowInSongsRequested(object? sender, Song track)
        {
            CloseQueuePopupIfFromQueuePopout(sender);
            if (track == null)
                return;

            ShowLibraryView();
            songsView?.SelectTrack(track);
        }

        private void OnShowInQueueRequested(object? sender, Song track)
        {
            CloseQueuePopupIfFromQueuePopout(sender);
            if (track == null)
                return;

            ShowQueueView();
            queueViewControl?.SelectTrack(track);
        }

        private void OnInfoRequested(object? sender, Song track)
        {
            if (track == null)
                return;
            ShowTrackInfoDialog(track);
        }

        private void OpenLaunchInfoDialog(string? launchSection = null)
        {
            var track = currentTrack ?? allTracks.FirstOrDefault();
            if (track == null)
            {
                MessageDialog.Show(this, "Song info", "Add music to your library first, or play a track.", MessageDialog.Buttons.Ok);
                return;
            }

            ShowTrackInfoDialog(track, launchSection);
        }

        private void ShowTrackInfoDialog(Song track, string? launchSection = null)
        {
            var infoWindow = new InfoMetadataView(launchSection)
            {
                Owner = this
            };
            infoWindow.ShowInSongsRequested += OnShowInSongsRequested;
            infoWindow.ShowInArtistsRequested += OnShowInArtistsRequested;
            infoWindow.ShowInAlbumsRequested += OnShowInAlbumsRequested;
            infoWindow.ReleasePlaybackForFile = ReleasePlaybackForMetadataWrite;
            infoWindow.RestorePlaybackAfterFile = RestorePlaybackAfterMetadataWrite;
            infoWindow.SavedMetadataToDisk += InfoWindow_SavedMetadataToDisk;
            TrackMetadataLoader.ReloadTagFieldsFromFile(track);
            infoWindow.LoadTrack(track, allTracks);

            infoWindow.Closed += async (_, _) =>
            {
                infoWindow.ShowInSongsRequested -= OnShowInSongsRequested;
                infoWindow.ShowInArtistsRequested -= OnShowInArtistsRequested;
                infoWindow.ShowInAlbumsRequested -= OnShowInAlbumsRequested;
                infoWindow.SavedMetadataToDisk -= InfoWindow_SavedMetadataToDisk;
                if (!infoWindow.MetadataSavedOnClose)
                    return;
                if (infoWindow.HostNotifyRefreshDone)
                    return;
                await UpdateLibraryCacheAsync();
                RefreshAfterMetadataEdit(track);
            };

            infoWindow.Show();
        }

        private async void InfoWindow_SavedMetadataToDisk(object? sender, Song track)
        {
            if (track == null)
                return;
            await UpdateLibraryCacheAsync();
            RefreshAfterMetadataEdit(track);
        }
    }
}
