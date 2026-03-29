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
        public event EventHandler<Song>? PlayNextRequested;
        public event EventHandler<Song>? AddToQueueRequested;
        public event EventHandler<Song>? InfoRequested;
        public event EventHandler<Song>? ShowInExplorerRequested;
        public event EventHandler<IReadOnlyList<Song>>? RemoveFromLibraryRequested;
        public event EventHandler<Song>? DeleteRequested;

        protected void WireTrackList()
        {
            TrackList.PlayTrackRequested           += (s, t) => PlayTrackRequested?.Invoke(this, t);
            TrackList.AddToPlaylistRequested      += (s, t) => AddToPlaylistRequested?.Invoke(this, t);
            TrackList.PlayNextRequested           += (s, t) => PlayNextRequested?.Invoke(this, t);
            TrackList.AddToQueueRequested         += (s, t) => AddToQueueRequested?.Invoke(this, t);
            TrackList.InfoRequested               += (s, t) => InfoRequested?.Invoke(this, t);
            TrackList.ShowInExplorerRequested     += (s, t) => ShowInExplorerRequested?.Invoke(this, t);
            TrackList.RemoveFromLibraryRequested  += (s, tracks) => RemoveFromLibraryRequested?.Invoke(this, tracks);
            TrackList.DeleteRequested             += (s, t) => DeleteRequested?.Invoke(this, t);
        }

        public void RebuildColumns() => TrackList.RebuildColumns();
    }
}

