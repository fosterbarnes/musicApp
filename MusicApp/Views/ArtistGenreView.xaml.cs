using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MusicApp.Views
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
        private readonly ObservableCollection<string> _namesList = new ObservableCollection<string>();

        public ArtistGenreView()
        {
            InitializeComponent();
            lstArtistsOrGenres.ItemsSource = _namesList;
            trackList.ViewName = "Songs";
            trackList.AddToPlaylistRequested += (s, track) => AddToPlaylistRequested?.Invoke(this, track);
            trackList.AddTrackToPlaylistRequested += (s, args) => AddTrackToPlaylistRequested?.Invoke(this, args);
            trackList.CreateNewPlaylistWithTrackRequested += (s, track) => CreateNewPlaylistWithTrackRequested?.Invoke(this, track);
            trackList.PlayNextRequested += (s, track) => PlayNextRequested?.Invoke(this, track);
            trackList.AddToQueueRequested += (s, track) => AddToQueueRequested?.Invoke(this, track);
            trackList.InfoRequested += (s, track) => InfoRequested?.Invoke(this, track);
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
                _allTracks = value;
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
        public event EventHandler<Song>? ShowInExplorerRequested;
        public event EventHandler<Song>? RemoveFromLibraryRequested;
        public event EventHandler<Song>? DeleteRequested;

        public void RebuildColumns() => trackList.RebuildColumns();

        /// <summary>Select an artist by name (for search navigation). No-op if not in Artists view or name not found.</summary>
        public void SelectArtist(string name)
        {
            if (!string.Equals(ViewName, "Artists", StringComparison.OrdinalIgnoreCase)) return;
            if (string.IsNullOrWhiteSpace(name)) return;
            if (_namesList.Contains(name))
                lstArtistsOrGenres.SelectedItem = name;
        }

        private static void OnViewNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ArtistGenreView view && e.NewValue is string name)
            {
                view.trackList.ViewName = "Songs";
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
            _namesList.Clear();
            if (_allTracks == null) return;

            var tracks = _allTracks.Cast<Song>().ToList();
            bool isArtists = string.Equals(ViewName, "Artists", StringComparison.OrdinalIgnoreCase);

            var names = isArtists
                ? tracks.Where(t => !string.IsNullOrWhiteSpace(t.Artist)).Select(t => t.Artist).Distinct().OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList()
                : tracks.Where(t => !string.IsNullOrWhiteSpace(t.Genre)).Select(t => t.Genre).Distinct().OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var name in names)
                _namesList.Add(name);
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
    }
}
