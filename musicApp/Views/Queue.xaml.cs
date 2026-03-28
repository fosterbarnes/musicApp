using System;
using System.Windows.Controls;

namespace musicApp.Views
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
            trackList.PlayTrackRequested += (_, track) => PlayTrackRequested?.Invoke(this, track);
            trackList.TrackRowsReordered += (_, args) => TracksReordered?.Invoke(this, args);
        }

        public System.Collections.IEnumerable? ItemsSource
        {
            get => trackList.ItemsSource;
            set => trackList.ItemsSource = value;
        }

        public event System.EventHandler<Song>? PlayTrackRequested;
        public event System.EventHandler<(int fromViewIndex, int toViewIndex)>? TracksReordered;

        public event EventHandler? QueueToolbarRemoveRequested;
        public event EventHandler? QueueToolbarMoveUpRequested;
        public event EventHandler? QueueToolbarMoveDownRequested;

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

        public Song? SelectedTrack => trackList.SelectedTrack;

        public int GetSelectedViewIndex()
        {
            if (trackList.SelectedTrack is not Song selected || trackList.ItemsSource == null)
                return -1;

            if (!string.IsNullOrWhiteSpace(selected.FilePath))
            {
                int i = 0;
                foreach (var item in trackList.ItemsSource)
                {
                    if (item is Song s && !string.IsNullOrWhiteSpace(s.FilePath) &&
                        string.Equals(s.FilePath, selected.FilePath, StringComparison.OrdinalIgnoreCase))
                        return i;
                    i++;
                }
            }

            int j = 0;
            foreach (var item in trackList.ItemsSource)
            {
                if (item is Song s &&
                    string.Equals(s.Title, selected.Title, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(s.Artist, selected.Artist, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(s.Album, selected.Album, StringComparison.OrdinalIgnoreCase))
                    return j;
                j++;
            }

            int k = 0;
            foreach (var item in trackList.ItemsSource)
            {
                if (ReferenceEquals(item, selected))
                    return k;
                k++;
            }

            return -1;
        }

        private void QueueToolbarRemove_Click(object sender, System.Windows.RoutedEventArgs e) =>
            QueueToolbarRemoveRequested?.Invoke(this, EventArgs.Empty);

        private void QueueToolbarMoveUp_Click(object sender, System.Windows.RoutedEventArgs e) =>
            QueueToolbarMoveUpRequested?.Invoke(this, EventArgs.Empty);

        private void QueueToolbarMoveDown_Click(object sender, System.Windows.RoutedEventArgs e) =>
            QueueToolbarMoveDownRequested?.Invoke(this, EventArgs.Empty);

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
    }
}
