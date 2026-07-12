using System;
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
        }

        public event EventHandler? QueueToolbarRemoveRequested;
        public event EventHandler? QueueToolbarMoveUpRequested;
        public event EventHandler? QueueToolbarMoveDownRequested;

        public int GetSelectedViewIndex()
        {
            if (trackList.SelectedTrack is not Song selected)
                return -1;
            return SongIdentity.FindViewIndex(trackList.ItemsSource, selected);
        }

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
