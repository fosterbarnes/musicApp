using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using musicApp.Helpers;

namespace musicApp.Views
{
    public partial class PlaylistsView : TrackListHostBase
    {
        private ObservableCollection<Song>? _libraryTracks;
        private INotifyCollectionChanged? _libraryCollectionNotify;

        protected override TrackListView TrackList => trackList;

        public PlaylistsView()
        {
            InitializeComponent();
            emptyLibraryOverlay.AddMusicFolderRequested += (_, __) => AddMusicFolderRequested?.Invoke(this, EventArgs.Empty);
            trackList.ContextMenuViewName = "Playlists";
            WireTrackList();
            trackList.RemoveFromPlaylistRequested += (s, args) => RemoveFromPlaylistRequested?.Invoke(this, args);
        }

        /// <summary>Main library track list; used to show the empty-library add-music affordance in the detail pane.</summary>
        public ObservableCollection<Song>? LibraryTracks
        {
            get => _libraryTracks;
            set
            {
                if (_libraryCollectionNotify != null)
                {
                    _libraryCollectionNotify.CollectionChanged -= OnLibraryTracksChanged;
                    _libraryCollectionNotify = null;
                }

                _libraryTracks = value;
                if (value is INotifyCollectionChanged incc)
                {
                    _libraryCollectionNotify = incc;
                    incc.CollectionChanged += OnLibraryTracksChanged;
                }

                ApplyPlaylistRightPaneState();
            }
        }

        public event EventHandler? AddMusicFolderRequested;

        public ObservableCollection<Playlist>? Playlists
        {
            get => lstPlaylists.ItemsSource as ObservableCollection<Playlist>;
            set => lstPlaylists.ItemsSource = value;
        }

        public Playlist? SelectedPlaylist => lstPlaylists.SelectedItem as Playlist;

        public void SelectTrack(Song track, bool grabFocus = false)
        {
            if (track == null) return;
            var matched = SongIdentity.FindInEnumerable(trackList.ItemsSource, track) ?? track;
            trackList.ScrollToSong(matched, grabFocus);
        }

        public event EventHandler<(Song track, Playlist playlist)>? RemoveFromPlaylistRequested;

        private void OnLibraryTracksChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            ApplyPlaylistRightPaneState();
        }

        private void LstPlaylists_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyPlaylistRightPaneState();
        }

        private void ApplyPlaylistRightPaneState()
        {
            if (EmptyLibraryAddOverlay.IsTrackLibraryEmpty(_libraryTracks))
            {
                trackList.CurrentPlaylist = null;
                trackList.ItemsSource = null;
                trackList.Visibility = Visibility.Collapsed;
                placeholderText.Visibility = Visibility.Collapsed;
                emptyLibraryOverlay.Visibility = Visibility.Visible;
                return;
            }

            emptyLibraryOverlay.Visibility = Visibility.Collapsed;

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
