using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace musicApp.Views
{
    public partial class PlaylistsView : UserControl
    {
        public PlaylistsView()
        {
            InitializeComponent();
            trackList.ContextMenuViewName = "Playlists";
            trackList.AddToPlaylistRequested += (s, track) => AddToPlaylistRequested?.Invoke(this, track);
            trackList.AddTrackToPlaylistRequested += (s, args) => AddTrackToPlaylistRequested?.Invoke(this, args);
            trackList.CreateNewPlaylistWithTrackRequested += (s, track) => CreateNewPlaylistWithTrackRequested?.Invoke(this, track);
            trackList.PlayNextRequested += (s, track) => PlayNextRequested?.Invoke(this, track);
            trackList.AddToQueueRequested += (s, track) => AddToQueueRequested?.Invoke(this, track);
            trackList.InfoRequested += (s, track) => InfoRequested?.Invoke(this, track);
            trackList.ShowInArtistsRequested += (s, track) => ShowInArtistsRequested?.Invoke(this, track);
            trackList.ShowInSongsRequested += (s, track) => ShowInSongsRequested?.Invoke(this, track);
            trackList.ShowInAlbumsRequested += (s, track) => ShowInAlbumsRequested?.Invoke(this, track);
            trackList.ShowInQueueRequested += (s, track) => ShowInQueueRequested?.Invoke(this, track);
            trackList.ShowInExplorerRequested += (s, track) => ShowInExplorerRequested?.Invoke(this, track);
            trackList.RemoveFromLibraryRequested += (s, track) => RemoveFromLibraryRequested?.Invoke(this, track);
            trackList.DeleteRequested += (s, track) => DeleteRequested?.Invoke(this, track);
            trackList.RemoveFromPlaylistRequested += (s, args) => RemoveFromPlaylistRequested?.Invoke(this, args);
        }

        public ObservableCollection<Playlist>? Playlists
        {
            get => lstPlaylists.ItemsSource as ObservableCollection<Playlist>;
            set => lstPlaylists.ItemsSource = value;
        }

        public Playlist? SelectedPlaylist => lstPlaylists.SelectedItem as Playlist;

        public event System.EventHandler<Song>? PlayTrackRequested;

        public event System.EventHandler<Song>? AddToPlaylistRequested;
        public event System.EventHandler<(Song track, Playlist playlist)>? AddTrackToPlaylistRequested;
        public event System.EventHandler<Song>? CreateNewPlaylistWithTrackRequested;
        public event System.EventHandler<Song>? PlayNextRequested;
        public event System.EventHandler<Song>? AddToQueueRequested;
        public event System.EventHandler<Song>? InfoRequested;
        public event System.EventHandler<Song>? ShowInArtistsRequested;
        public event System.EventHandler<Song>? ShowInSongsRequested;
        public event System.EventHandler<Song>? ShowInAlbumsRequested;
        public event System.EventHandler<Song>? ShowInQueueRequested;
        public event System.EventHandler<Song>? ShowInExplorerRequested;
        public event System.EventHandler<Song>? RemoveFromLibraryRequested;
        public event System.EventHandler<Song>? DeleteRequested;
        public event System.EventHandler<(Song track, Playlist playlist)>? RemoveFromPlaylistRequested;

        private void LstPlaylists_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstPlaylists.SelectedItem is Playlist playlist)
            {
                trackList.CurrentPlaylist = playlist;
                trackList.ItemsSource = playlist.Tracks;
                trackList.Visibility = Visibility.Visible;
                placeholderText.Visibility = Visibility.Collapsed;
            }
            else
            {
                trackList.CurrentPlaylist = null;
                trackList.ItemsSource = null;
                trackList.Visibility = Visibility.Collapsed;
                placeholderText.Visibility = Visibility.Visible;
            }
        }

        private void TrackList_PlayTrackRequested(object? sender, Song e)
        {
            PlayTrackRequested?.Invoke(this, e);
        }

        public event EventHandler? CreatePlaylistRequested;
        public event EventHandler? ImportPlaylistRequested;
        public event EventHandler<Playlist>? ExportPlaylistRequested;
        public event EventHandler<Playlist>? DeletePlaylistRequested;
        public event EventHandler<(Playlist playlist, bool isPinned)>? PlaylistPinnedChanged;

        /// <summary>Selects the given playlist in the list and scrolls it into view.</summary>
        public void SelectPlaylist(Playlist? playlist)
        {
            lstPlaylists.SelectedItem = playlist;
            if (playlist != null)
                lstPlaylists.ScrollIntoView(playlist);
        }

        public void RefreshTrackListBindings() => trackList.RefreshItemBindings();

        private void CreatePlaylist_Click(object sender, RoutedEventArgs e)
        {
            CreatePlaylistRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ImportPlaylist_Click(object sender, RoutedEventArgs e)
        {
            ImportPlaylistRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ExportPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (lstPlaylists.SelectedItem is Playlist playlist)
            {
                ExportPlaylistRequested?.Invoke(this, playlist);
            }
        }

        private void LstPlaylists_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (lstPlaylists.SelectedItem is not Playlist)
            {
                e.Handled = true;
            }
        }

        private void DeletePlaylistMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (lstPlaylists.SelectedItem is Playlist playlist)
            {
                DeletePlaylistRequested?.Invoke(this, playlist);
            }
        }

        private void DeletePlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (lstPlaylists.SelectedItem is Playlist playlist)
            {
                DeletePlaylistRequested?.Invoke(this, playlist);
            }
        }

        private void PlaylistPinToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is Playlist playlist)
            {
                playlist.IsPinned = !playlist.IsPinned;
                PlaylistPinnedChanged?.Invoke(this, (playlist, playlist.IsPinned));
            }
        }
    }
}
