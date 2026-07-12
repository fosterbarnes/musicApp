using System;
using System.Collections.Generic;
using System.Linq;

namespace musicApp.Helpers;

internal static class ArtistPlaybackOrder
{
    public static List<Song> BuildOrderedArtistTracks(IEnumerable<Song> allTracks, string artist)
    {
        if (string.IsNullOrWhiteSpace(artist))
            return new List<Song>();

        return AlbumGroupedPlaybackOrder.BuildOrderedTracks(
            allTracks,
            t => string.Equals(t.Artist, artist, StringComparison.OrdinalIgnoreCase));
    }

    public static int IndexOfTrackInOrderedList(IReadOnlyList<Song> ordered, Song selected) =>
        SongIdentity.IndexOf(ordered, selected);
}
