using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using musicApp.Helpers;

namespace musicApp.Views
{
    public partial class ArtistGenreView : TrackListHostBase
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
        private INotifyCollectionChanged? _libraryCollectionNotify;
        private readonly ObservableCollection<string> _namesList = new ObservableCollection<string>();

        protected override TrackListView TrackList => trackList;

        public ArtistGenreView()
        {
            InitializeComponent();
            lstArtistsOrGenres.ItemsSource = _namesList;
            trackList.ViewName = "Songs";
            trackList.ContextMenuViewName = ViewName;
            WireTrackList();
            emptyLibraryOverlay.AddMusicFolderRequested += (_, __) => AddMusicFolderRequested?.Invoke(this, EventArgs.Empty);
            Loaded += (_, _) => UpdateSidebarTitleAndPlaceholder();
        }

        public event EventHandler? AddMusicFolderRequested;

        /// <summary>Full library of tracks. Used to build artist/genre list and to filter when one is selected.</summary>
        public new IEnumerable? ItemsSource
        {
            get => _allTracks;
            set
            {
                int newCount = value is ICollection col ? col.Count : -1;
                if (ReferenceEquals(_allTracks, value) && newCount == _itemsSourceCount)
                    return;

                if (_libraryCollectionNotify != null)
                {
                    _libraryCollectionNotify.CollectionChanged -= OnLibraryCollectionChanged;
                    _libraryCollectionNotify = null;
                }

                _allTracks = value;
                _itemsSourceCount = newCount;
                if (value is INotifyCollectionChanged incc)
                {
                    _libraryCollectionNotify = incc;
                    incc.CollectionChanged += OnLibraryCollectionChanged;
                }

                RefreshNamesList();
                ApplySidebarSelectionToRightPane();
            }
        }

        /// <summary>
        /// Selects the track's artist in the sidebar and highlights that track in the right-side track list.
        /// </summary>
        public void SelectTrack(Song track, bool grabFocus = false)
        {
            if (track == null) return;

            if (string.Equals(ViewName, "Artists", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(track.Artist)) return;
                SelectArtist(track.Artist);
            }
            else if (string.Equals(ViewName, "Genres", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(track.Genre)) return;
                SelectGenre(track.Genre);
            }
            else
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                var matched = FindTrackInCurrentList(track);
                if (matched != null)
                    trackList.ScrollToSong(matched, grabFocus);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>Select an artist by name and scroll it into view. No-op if not in Artists view or name not found.</summary>
        public void SelectArtist(string name)
        {
            if (!string.Equals(ViewName, "Artists", StringComparison.OrdinalIgnoreCase)) return;
            if (string.IsNullOrWhiteSpace(name)) return;
            var match = _namesList.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                lstArtistsOrGenres.SelectedItem = match;
                lstArtistsOrGenres.ScrollIntoView(match);
            }
        }

        /// <summary>Select a genre by name and scroll it into view. No-op if not in Genres view or name not found.</summary>
        public void SelectGenre(string name)
        {
            if (!string.Equals(ViewName, "Genres", StringComparison.OrdinalIgnoreCase)) return;
            if (string.IsNullOrWhiteSpace(name)) return;
            var match = _namesList.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                lstArtistsOrGenres.SelectedItem = match;
                lstArtistsOrGenres.ScrollIntoView(match);
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

            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in tracks)
            {
                var name = isArtists ? t.Artist : t.Genre;
                if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
                    continue;
                names.Add(name);
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (var name in names)
                _namesList.Add(name);

            if (!string.IsNullOrEmpty(prevSelected))
            {
                var match = _namesList.FirstOrDefault(n => string.Equals(n, prevSelected, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    lstArtistsOrGenres.SelectedItem = match;
            }
        }

        private void OnLibraryCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            _itemsSourceCount = _allTracks is ICollection c ? c.Count : _itemsSourceCount;
            RefreshNamesList();
            ApplySidebarSelectionToRightPane();
        }

        private void LstArtistsOrGenres_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplySidebarSelectionToRightPane();
        }

        private void ApplySidebarSelectionToRightPane()
        {
            if (EmptyLibraryAddOverlay.IsTrackLibraryEmpty(_allTracks))
            {
                trackList.CurrentPlaylist = null;
                trackList.ItemsSource = null;
                trackList.Visibility = Visibility.Collapsed;
                placeholderText.Visibility = Visibility.Collapsed;
                emptyLibraryOverlay.Visibility = Visibility.Visible;
                return;
            }

            emptyLibraryOverlay.Visibility = Visibility.Collapsed;

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
                ? tracks.Where(t => string.Equals(t.Artist, selectedName, StringComparison.OrdinalIgnoreCase)).ToList()
                : tracks.Where(t => string.Equals(t.Genre, selectedName, StringComparison.OrdinalIgnoreCase)).ToList();

            trackList.CurrentPlaylist = null;
            trackList.ItemsSource = filtered;
            trackList.Visibility = Visibility.Visible;
            placeholderText.Visibility = Visibility.Collapsed;
        }

        private Song? FindTrackInCurrentList(Song track) =>
            SongIdentity.FindInEnumerable(trackList.ItemsSource, track);
    }
}
