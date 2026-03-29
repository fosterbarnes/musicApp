using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace musicApp.Views
{
    public partial class SongsView : UserControl
    {
        public static readonly DependencyProperty IsLibraryEmptyProperty = DependencyProperty.Register(
            nameof(IsLibraryEmpty), typeof(bool), typeof(SongsView), new PropertyMetadata(true));

        private INotifyCollectionChanged? _itemsSourceCollection;

        public SongsView()
        {
            InitializeComponent();

            trackList.AddToPlaylistRequested += (s, track) => AddToPlaylistRequested?.Invoke(this, track);
            trackList.AddTrackToPlaylistRequested += (s, args) => AddTrackToPlaylistRequested?.Invoke(this, args);
            trackList.CreateNewPlaylistWithTrackRequested += (s, track) => CreateNewPlaylistWithTrackRequested?.Invoke(this, track);
            trackList.PlayNextRequested += (s, track) => PlayNextRequested?.Invoke(this, track);
            trackList.AddToQueueRequested += (s, track) => AddToQueueRequested?.Invoke(this, track);
            trackList.InfoRequested += (s, track) => InfoRequested?.Invoke(this, track);
            trackList.ShowInArtistsRequested += (s, track) => ShowInArtistsRequested?.Invoke(this, track);
            trackList.ShowInAlbumsRequested += (s, track) => ShowInAlbumsRequested?.Invoke(this, track);
            trackList.ShowInQueueRequested += (s, track) => ShowInQueueRequested?.Invoke(this, track);
            trackList.ShowInExplorerRequested += (s, track) => ShowInExplorerRequested?.Invoke(this, track);
            trackList.RemoveFromLibraryRequested += (s, tracks) => RemoveFromLibraryRequested?.Invoke(this, tracks);
            trackList.DeleteRequested += (s, track) => DeleteRequested?.Invoke(this, track);
        }

        public bool IsLibraryEmpty
        {
            get => (bool)GetValue(IsLibraryEmptyProperty);
            set => SetValue(IsLibraryEmptyProperty, value);
        }

        public System.Collections.IEnumerable? ItemsSource
        {
            get => trackList.ItemsSource;
            set
            {
                if (_itemsSourceCollection != null)
                {
                    _itemsSourceCollection.CollectionChanged -= OnItemsSourceCollectionChanged;
                    _itemsSourceCollection = null;
                }
                trackList.ItemsSource = value;
                UpdateIsLibraryEmpty(value);
                if (value is INotifyCollectionChanged incc)
                {
                    _itemsSourceCollection = incc;
                    incc.CollectionChanged += OnItemsSourceCollectionChanged;
                }
            }
        }

        public event System.EventHandler? AddMusicFolderRequested;

        public event System.EventHandler<Song>? PlayTrackRequested;

        public event System.EventHandler<Song>? AddToPlaylistRequested;
        public event System.EventHandler<(Song track, Playlist playlist)>? AddTrackToPlaylistRequested;
        public event System.EventHandler<Song>? CreateNewPlaylistWithTrackRequested;
        public event System.EventHandler<Song>? PlayNextRequested;
        public event System.EventHandler<Song>? AddToQueueRequested;
        public event System.EventHandler<Song>? InfoRequested;
        public event System.EventHandler<Song>? ShowInArtistsRequested;
        public event System.EventHandler<Song>? ShowInAlbumsRequested;
        public event System.EventHandler<Song>? ShowInQueueRequested;
        public event System.EventHandler<Song>? ShowInExplorerRequested;
        public event System.EventHandler<IReadOnlyList<Song>>? RemoveFromLibraryRequested;
        public event System.EventHandler<Song>? DeleteRequested;

        public void RebuildColumns() => trackList.RebuildColumns();

        public void RefreshTrackListBindings() => trackList.RefreshItemBindings();

        public void ScrollToSong(Song song) => trackList.ScrollToSong(song);

        /// <summary>
        /// Selects (highlights) the matching song in the list and scrolls it into view.
        /// Matching prefers FilePath when available.
        /// </summary>
        public void SelectTrack(Song track)
        {
            if (track == null)
                return;

            if (trackList.ItemsSource == null)
            {
                trackList.ScrollToSong(track);
                return;
            }

            // Prefer file-path matching so selection works even if the Song instance differs.
            if (!string.IsNullOrWhiteSpace(track.FilePath))
            {
                foreach (var item in trackList.ItemsSource)
                {
                    if (item is not Song s) continue;
                    if (!string.IsNullOrWhiteSpace(s.FilePath) &&
                        string.Equals(s.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        trackList.ScrollToSong(s);
                        return;
                    }
                }
            }

            // Fallback: title/artist/album.
            foreach (var item in trackList.ItemsSource)
            {
                if (item is not Song s) continue;
                if (string.Equals(s.Title, track.Title, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(s.Artist, track.Artist, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(s.Album, track.Album, StringComparison.OrdinalIgnoreCase))
                {
                    trackList.ScrollToSong(s);
                    return;
                }
            }

            // Last resort: set selection to the provided instance.
            trackList.ScrollToSong(track);
        }

        public Song? SelectedTrack => trackList.SelectedTrack;

        private void TrackList_PlayTrackRequested(object? sender, Song e)
        {
            PlayTrackRequested?.Invoke(this, e);
        }

        private void OnItemsSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateIsLibraryEmpty(trackList.ItemsSource);
        }

        private void UpdateIsLibraryEmpty(System.Collections.IEnumerable? source)
        {
            bool empty = true;
            if (source != null)
            {
                if (source is ICollection col)
                    empty = col.Count == 0;
                else
                {
                    var e = source.GetEnumerator();
                    try { empty = !e.MoveNext(); }
                    finally { if (e is IDisposable d) d.Dispose(); }
                }
            }
            IsLibraryEmpty = empty;
        }

        private void EmptyOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            AddMusicFolderRequested?.Invoke(this, System.EventArgs.Empty);
        }
    }
}
