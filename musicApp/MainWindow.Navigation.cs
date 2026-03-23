using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MusicApp.Helpers;
using MusicApp.Views;

namespace MusicApp
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

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsView
            {
                Owner = this
            };
            settingsWindow.ShowDialog();
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

        private void ShowAlbumsView()
        {
            contentHost.Content = albumsViewControl;
            SetSidebarNavActive(btnAlbums);
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
                         btnArtists, btnAlbums, btnLibrary, btnGenres, btnPlaylists, btnRecentlyPlayed, btnQueue
                     })
                SidebarNav.SetIsActive(b, false);
            if (activeButton != null)
                SidebarNav.SetIsActive(activeButton, true);
        }

        private void OnShowInExplorerRequested(object? sender, Song track)
        {
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
            if (track == null || string.IsNullOrWhiteSpace(track.Artist))
                return;

            ShowArtistsView();
            artistsViewControl?.SelectTrack(track);
        }

        private void OnShowInAlbumsRequested(object? sender, Song track)
        {
            if (track == null || string.IsNullOrWhiteSpace(track.Album))
                return;

            ShowAlbumsView();
            albumsViewControl?.SelectAlbum(track);
        }

        private void OnShowInSongsRequested(object? sender, Song track)
        {
            if (track == null)
                return;

            ShowLibraryView();
            songsView?.SelectTrack(track);
        }

        private void OnShowInQueueRequested(object? sender, Song track)
        {
            if (track == null)
                return;

            ShowQueueView();
            queueViewControl?.SelectTrack(track);
        }

        private async void OnInfoRequested(object? sender, Song track)
        {
            if (track == null)
                return;

            var infoWindow = new InfoMetadataView
            {
                Owner = this
            };
            infoWindow.ShowInSongsRequested += OnShowInSongsRequested;
            infoWindow.ShowInArtistsRequested += OnShowInArtistsRequested;
            infoWindow.ShowInAlbumsRequested += OnShowInAlbumsRequested;
            infoWindow.ReleasePlaybackForFile = ReleasePlaybackForMetadataWrite;
            infoWindow.RestorePlaybackAfterFile = RestorePlaybackAfterMetadataWrite;
            TrackMetadataLoader.ReloadTagFieldsFromFile(track);
            infoWindow.LoadTrack(track, allTracks);
            var saved = infoWindow.ShowDialog() == true;
            if (saved)
            {
                await UpdateLibraryCacheAsync();
                RefreshAfterMetadataEdit(track);
            }
        }
    }
}
