using System;
using System.Diagnostics;
using System.IO;
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
            w.VolumeChanged += TitleBarPlayer_VolumeChanged;
            w.SwapCurrentNextRequested += (_, _) => _player.SwapCurrentAndNext();
            w.Closed += MiniPlayerWindow_Closed;
            try { w.SetVolumeWithoutNotify(titleBarPlayer.Volume); } catch { }
            w.Show();
            PushMiniPlayerState(w);
            // Saved now too, so a crash or force-kill still brings it back next launch.
            appSettings.MiniPlayerWindow.IsOpen = true;
            _ = settingsManager.UpdateSettingsAsync(s => s.MiniPlayerWindow.IsOpen = true);
        }

        private void RestoreMiniPlayerWindowState(MiniPlayerWindow window, SettingsManager.MiniPlayerWindowStateSettings state)
        {
            // Never placed, or saved fully off every screen: center on the main window, then fit.
            double left = Left + (ActualWidth - state.Width) / 2;
            double top = Top + (ActualHeight - state.Height) / 2;
            if (state is { Left: { } savedLeft, Top: { } savedTop } &&
                MonitorWorkArea.IsOnScreen(this, savedLeft, savedTop, state.Width, state.Height))
                (left, top) = (savedLeft, savedTop);
            if (!MonitorWorkArea.TryFit(this, left, top, state.Width, state.Height,
                    new Size(window.MinWidth, window.MinHeight), out var fitted))
                return;

            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Width = fitted.Width;
            window.Height = fitted.Height;
            window.Left = fitted.Left;
            window.Top = fitted.Top;
        }

        private async void MiniPlayerWindow_Closed(object? sender, EventArgs e)
        {
            if (sender is not MiniPlayerWindow window)
                return;

            if (!ReferenceEquals(_miniPlayerWindow, window))
                return;

            _miniPlayerWindow = null;
            // Closing the app closes owned windows too; shutdown already saved it as open.
            if (_shutdownCloseFinalized)
                return;

            CaptureMiniPlayerWindowState(window, isOpen: false);
            var miniPlayer = appSettings.MiniPlayerWindow;
            await settingsManager.UpdateSettingsAsync(s => s.MiniPlayerWindow = miniPlayer);
        }

        /// <summary>Minimized: its pre-minimize bounds. Gone or unreadable: the last saved bounds.</summary>
        private void CaptureMiniPlayerWindowState(MiniPlayerWindow? window, bool isOpen)
        {
            var saved = appSettings.MiniPlayerWindow;
            var state = new SettingsManager.MiniPlayerWindowStateSettings
            {
                Width = saved.Width, Height = saved.Height, Left = saved.Left, Top = saved.Top, IsOpen = isOpen
            };
            var bounds = window == null ? Rect.Empty
                : window.WindowState == WindowState.Normal
                    ? new Rect(window.Left, window.Top, window.Width, window.Height)
                    : window.RestoreBounds;
            if (!bounds.IsEmpty && double.IsFinite(bounds.X) && double.IsFinite(bounds.Y) &&
                double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height))
            {
                state.Width = bounds.Width;
                state.Height = bounds.Height;
                state.Left = bounds.X;
                state.Top = bounds.Y;
            }
            appSettings.MiniPlayerWindow = state;
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
            var track = CurrentTrack ?? allTracks.FirstOrDefault();
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
