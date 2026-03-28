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

        private void ShowLibraryView()
        {
            contentHost.Content = songsView;
            SetSidebarNavActive(btnLibrary);
        }

        private void ShowQueueView()
        {
            contentHost.Content = queueViewControl;
            UpdateQueueView();
            SetSidebarNavActive(btnQueue);
        }

        private void ShowPlaylistsView(Playlist? selectPlaylist = null)
        {
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
            contentHost.Content = recentlyPlayedViewControl;
            SetSidebarNavActive(btnRecentlyPlayed);
        }

        private void ShowArtistsView()
        {
            contentHost.Content = artistsViewControl;
            SetSidebarNavActive(btnArtists);
        }

        /// <param name="bindFullLibrary">False when caller assigns a narrower ItemsSource (e.g. search subset).</param>
        private void ShowAlbumsView(bool bindFullLibrary = true)
        {
            if (albumsViewControl != null)
                albumsViewControl.BrowseMode = AlbumsBrowseMode.AllAlbums;
            if (bindFullLibrary && albumsViewControl != null)
                albumsViewControl.ItemsSource = allTracks;
            contentHost.Content = albumsViewControl;
            SetSidebarNavActive(btnAlbums);
        }

        private void ShowRecentlyAddedView()
        {
            if (albumsViewControl == null)
                return;
            albumsViewControl.BrowseMode = AlbumsBrowseMode.RecentlyAdded;
            albumsViewControl.ItemsSource = allTracks;
            contentHost.Content = albumsViewControl;
            SetSidebarNavActive(btnRecentlyAdded);
        }

        private void ShowGenresView()
        {
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
