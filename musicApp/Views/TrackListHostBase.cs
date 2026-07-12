using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows.Controls;
using musicApp;

namespace musicApp.Views
{
    public abstract class TrackListHostBase : UserControl
    {
        protected abstract TrackListView TrackList { get; }

        public IEnumerable? ItemsSource
        {
            get => TrackList.ItemsSource;
            set => TrackList.ItemsSource = value;
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
        public event EventHandler<IReadOnlyList<Song>>? RemoveFromLibraryRequested;
        public event EventHandler<Song>? DeleteRequested;
        public event EventHandler<(int fromViewIndex, int toViewIndex)>? TracksReordered;

        protected void WireTrackList()
        {
            var tl = TrackList;
            tl.PlayTrackRequested += (s, t) => PlayTrackRequested?.Invoke(this, t);
            tl.AddToPlaylistRequested += (s, t) => AddToPlaylistRequested?.Invoke(this, t);
            tl.AddTrackToPlaylistRequested += (s, a) => AddTrackToPlaylistRequested?.Invoke(this, a);
            tl.CreateNewPlaylistWithTrackRequested += (s, t) => CreateNewPlaylistWithTrackRequested?.Invoke(this, t);
            tl.PlayNextRequested += (s, t) => PlayNextRequested?.Invoke(this, t);
            tl.AddToQueueRequested += (s, t) => AddToQueueRequested?.Invoke(this, t);
            tl.InfoRequested += (s, t) => InfoRequested?.Invoke(this, t);
            tl.ShowInArtistsRequested += (s, t) => ShowInArtistsRequested?.Invoke(this, t);
            tl.ShowInSongsRequested += (s, t) => ShowInSongsRequested?.Invoke(this, t);
            tl.ShowInAlbumsRequested += (s, t) => ShowInAlbumsRequested?.Invoke(this, t);
            tl.ShowInQueueRequested += (s, t) => ShowInQueueRequested?.Invoke(this, t);
            tl.ShowInExplorerRequested += (s, t) => ShowInExplorerRequested?.Invoke(this, t);
            tl.RemoveFromLibraryRequested += (s, tracks) => RemoveFromLibraryRequested?.Invoke(this, tracks);
            tl.DeleteRequested += (s, t) => DeleteRequested?.Invoke(this, t);
            tl.TrackRowsReordered += (s, e) => TracksReordered?.Invoke(this, e);
        }

        public void RebuildColumns() => TrackList.RebuildColumns();
        public void RefreshTrackListBindings() => TrackList.RefreshItemBindings();
        public Song? SelectedTrack => TrackList.SelectedTrack;
    }
}
