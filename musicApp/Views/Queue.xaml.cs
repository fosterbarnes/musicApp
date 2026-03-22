using System;
using System.Windows.Controls;

namespace MusicApp.Views
{
    public partial class QueueView : UserControl
    {
        public QueueView()
        {
            InitializeComponent();
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
        }

        public System.Collections.IEnumerable? ItemsSource
        {
            get => trackList.ItemsSource;
            set => trackList.ItemsSource = value;
        }

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

        public void RebuildColumns() => trackList.RebuildColumns();

        public void RefreshTrackListBindings() => trackList.RefreshItemBindings();

        public void SelectTrack(Song track)
        {
            if (track == null || trackList.ItemsSource == null)
                return;

            foreach (var item in trackList.ItemsSource)
            {
                if (item is not Song s)
                    continue;

                if (!string.IsNullOrWhiteSpace(track.FilePath) &&
                    !string.IsNullOrWhiteSpace(s.FilePath) &&
                    string.Equals(s.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    trackList.ScrollToSong(s);
                    return;
                }
            }

            foreach (var item in trackList.ItemsSource)
            {
                if (item is not Song s)
                    continue;

                if (string.Equals(s.Title, track.Title, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(s.Artist, track.Artist, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(s.Album, track.Album, StringComparison.OrdinalIgnoreCase))
                {
                    trackList.ScrollToSong(s);
                    return;
                }
            }
        }

        private void TrackList_PlayTrackRequested(object? sender, Song e)
        {
            PlayTrackRequested?.Invoke(this, e);
        }
    }
}
