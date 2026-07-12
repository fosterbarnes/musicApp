using System;
using System.Collections.Generic;
using musicApp.Helpers;

namespace musicApp.Views
{
    public partial class RecentlyPlayedView : TrackListHostBase
    {
        protected override TrackListView TrackList => trackList;

        public RecentlyPlayedView()
        {
            InitializeComponent();
            WireTrackList();
        }

        public void SelectTrack(Song track, bool grabFocus = false)
        {
            if (track == null) return;
            var matched = SongIdentity.FindInEnumerable(trackList.ItemsSource, track) ?? track;
            trackList.ScrollToSong(matched, grabFocus);
        }
    }
}
