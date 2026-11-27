using System;
using System.Collections;
using System.Collections.Generic;
using musicApp.Helpers;

namespace musicApp.Views
{
    public partial class QueueView : TrackListHostBase
    {
        protected override TrackListView TrackList => trackList;

        public QueueView()
        {
            InitializeComponent();
            WireTrackList();
            trackList.RemoveFromQueueRequested += (_, e) => QueueToolbarRemoveRequested?.Invoke(this, e);
        }

        public event EventHandler? QueueToolbarRemoveRequested;
        public event EventHandler? QueueToolbarMoveUpRequested;
        public event EventHandler? QueueToolbarMoveDownRequested;

        // ItemsSource is the queue view, so its positions are queue view indexes
        public List<int> GetSelectedViewIndexes() => trackList.ItemsSource is IList items
            ? trackList.GetSelectedTracks().Select(items.IndexOf).Where(i => i >= 0).ToList()
            : [];

        private void QueueToolbarRemove_Click(object sender, System.Windows.RoutedEventArgs e) =>
            QueueToolbarRemoveRequested?.Invoke(this, EventArgs.Empty);

        private void QueueToolbarMoveUp_Click(object sender, System.Windows.RoutedEventArgs e) =>
            QueueToolbarMoveUpRequested?.Invoke(this, EventArgs.Empty);

        private void QueueToolbarMoveDown_Click(object sender, System.Windows.RoutedEventArgs e) =>
            QueueToolbarMoveDownRequested?.Invoke(this, EventArgs.Empty);

        public void SelectTrack(Song track, bool grabFocus = false)
        {
            if (track == null || trackList.ItemsSource == null)
                return;

            var matched = SongIdentity.FindInEnumerable(trackList.ItemsSource, track);
            if (matched != null)
                trackList.ScrollToSong(matched, grabFocus);
        }
    }
}
