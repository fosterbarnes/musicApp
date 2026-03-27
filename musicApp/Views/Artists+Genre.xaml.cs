using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace musicApp.Views
{
    public partial class ArtistGenreView : UserControl
    {
        public static readonly DependencyProperty ViewNameProperty = DependencyProperty.Register(
            nameof(ViewName), typeof(string), typeof(ArtistGenreView),
            new PropertyMetadata("Artists", OnViewNameChanged));

        public string ViewName
        {
            get => (string)GetValue(ViewNameProperty);
            set => SetValue(ViewNameProperty, value);
        }

        private IEnumerable? _allTracks;
        private int _itemsSourceCount = -1;
        private readonly ObservableCollection<string> _namesList = new ObservableCollection<string>();

        public ArtistGenreView()
        {
            InitializeComponent();
            lstArtistsOrGenres.ItemsSource = _namesList;
            trackList.ViewName = "Songs";
            trackList.ContextMenuViewName = ViewName;
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
            Loaded += (_, _) => UpdateSidebarTitleAndPlaceholder();
        }

        /// <summary>Full library of tracks. Used to build artist/genre list and to filter when one is selected.</summary>
        public IEnumerable? ItemsSource
        {
            get => _allTracks;
            set
            {
                int newCount = value is ICollection col ? col.Count : -1;
                if (ReferenceEquals(_allTracks, value) && newCount == _itemsSourceCount)
                    return;

                _allTracks = value;
                _itemsSourceCount = newCount;
                RefreshNamesList();
            }
        }

        public event EventHandler<Song>? PlayTrackRequested;
        public event EventHandler<Song>? AddToPlaylistRequested;
        public event EventHandler<(Song track, Playlist playlist)>? AddTrackToPlaylistRequested;
        public event EventHandler<Song>? CreateNewPlaylistWithTrackRequested;
        public event EventHandler<Song>? PlayNextRequested;
        public event EventHandler<Song>? AddToQueueRequested;
        public event EventHandler<Song>? InfoRequested;
        public event EventHandler<Song>? ShowInArtistsRequested;
        public event EventHandler<Song>? ShowInSongsRequested;
        public event EventHandler<Song>? ShowInAlbumsRequested;
        public event EventHandler<Song>? ShowInQueueRequested;
        public event EventHandler<Song>? ShowInExplorerRequested;
        public event EventHandler<Song>? RemoveFromLibraryRequested;
        public event EventHandler<Song>? DeleteRequested;

        public void RebuildColumns() => trackList.RebuildColumns();

        public void RefreshTrackListBindings() => trackList.RefreshItemBindings();

        /// <summary>
        /// Selects the track's artist in the sidebar and highlights that track in the right-side track list.
        /// </summary>
        public void SelectTrack(Song track)
        {
            if (track == null) return;
            if (!string.Equals(ViewName, "Artists", StringComparison.OrdinalIgnoreCase)) return;
            if (string.IsNullOrWhiteSpace(track.Artist)) return;

            SelectArtist(track.Artist);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var matched = FindTrackInCurrentList(track);
                if (matched != null)
                    trackList.ScrollToSong(matched);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>Select an artist by name and scroll it into view. No-op if not in Artists view or name not found.</summary>
        public void SelectArtist(string name)
        {
            if (!string.Equals(ViewName, "Artists", StringComparison.OrdinalIgnoreCase)) return;
            if (string.IsNullOrWhiteSpace(name)) return;
            if (_namesList.Contains(name))
            {
                lstArtistsOrGenres.SelectedItem = name;
                lstArtistsOrGenres.ScrollIntoView(name);
            }
        }

        /// <summary>Select a genre by name and scroll it into view. No-op if not in Genres view or name not found.</summary>
        public void SelectGenre(string name)
        {
            if (!string.Equals(ViewName, "Genres", StringComparison.OrdinalIgnoreCase)) return;
            if (string.IsNullOrWhiteSpace(name)) return;
            if (_namesList.Contains(name))
            {
                lstArtistsOrGenres.SelectedItem = name;
                lstArtistsOrGenres.ScrollIntoView(name);
            }
        }

        private static void OnViewNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ArtistGenreView view && e.NewValue is string name)
            {
                view.trackList.ViewName = "Songs";
                view.trackList.ContextMenuViewName = name;
                view.UpdateSidebarTitleAndPlaceholder();
                view.RefreshNamesList();
            }
        }

        private void UpdateSidebarTitleAndPlaceholder()
        {
            bool isArtists = string.Equals(ViewName, "Artists", StringComparison.OrdinalIgnoreCase);
            if (sidebarTitle != null)
                sidebarTitle.Text = isArtists ? "Artists" : "Genres";
            if (placeholderText != null)
                placeholderText.Text = isArtists ? "Select an artist" : "Select a genre";
        }

        private void RefreshNamesList()
        {
            var prevSelected = lstArtistsOrGenres.SelectedItem as string;
            _namesList.Clear();
            if (_allTracks == null) return;

            var tracks = _allTracks.Cast<Song>().ToList();
            bool isArtists = string.Equals(ViewName, "Artists", StringComparison.OrdinalIgnoreCase);

            var names = isArtists
                ? tracks.Where(t => !string.IsNullOrWhiteSpace(t.Artist)).Select(t => t.Artist).Distinct().OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList()
                : tracks.Where(t => !string.IsNullOrWhiteSpace(t.Genre)).Select(t => t.Genre).Distinct().OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var name in names)
                _namesList.Add(name);

            if (!string.IsNullOrEmpty(prevSelected) && _namesList.Contains(prevSelected))
                lstArtistsOrGenres.SelectedItem = prevSelected;
        }

        private void LstArtistsOrGenres_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstArtistsOrGenres.SelectedItem is not string selectedName || _allTracks == null)
            {
                trackList.CurrentPlaylist = null;
                trackList.ItemsSource = null;
                trackList.Visibility = Visibility.Collapsed;
                placeholderText.Visibility = Visibility.Visible;
                return;
            }

            var tracks = _allTracks.Cast<Song>().ToList();
            bool isArtists = string.Equals(ViewName, "Artists", StringComparison.OrdinalIgnoreCase);
            var filtered = isArtists
                ? tracks.Where(t => string.Equals(t.Artist, selectedName, StringComparison.Ordinal)).ToList()
                : tracks.Where(t => string.Equals(t.Genre, selectedName, StringComparison.Ordinal)).ToList();

            trackList.CurrentPlaylist = null;
            trackList.ItemsSource = filtered;
            trackList.Visibility = Visibility.Visible;
            placeholderText.Visibility = Visibility.Collapsed;
        }

        private void TrackList_PlayTrackRequested(object? sender, Song e)
        {
            PlayTrackRequested?.Invoke(this, e);
        }

        private Song? FindTrackInCurrentList(Song track)
        {
            if (trackList.ItemsSource is not IEnumerable items)
                return null;

            Song? fallback = null;
            foreach (var item in items)
            {
                if (item is not Song song)
                    continue;

                if (ReferenceEquals(song, track))
                    return song;

                if (!string.IsNullOrWhiteSpace(song.FilePath) &&
                    !string.IsNullOrWhiteSpace(track.FilePath) &&
                    string.Equals(song.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    return song;
                }

                if (fallback == null &&
                    string.Equals(song.Title, track.Title, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(song.Album, track.Album, StringComparison.OrdinalIgnoreCase))
                {
                    fallback = song;
                }
            }

            return fallback;
        }
    }
}
