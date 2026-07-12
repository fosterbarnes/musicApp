using System;
using System.Collections.Generic;

namespace musicApp.Helpers;

internal static class GenrePlaybackOrder
{
    public static List<Song> BuildOrderedGenreTracks(IEnumerable<Song> allTracks, string genre)
    {
        if (string.IsNullOrWhiteSpace(genre))
            return new List<Song>();

        return AlbumGroupedPlaybackOrder.BuildOrderedTracks(
            allTracks,
            t => string.Equals(t.Genre, genre, StringComparison.OrdinalIgnoreCase));
    }
}
