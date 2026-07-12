using System;
using System.Collections.Generic;
using System.Linq;

namespace musicApp.Helpers;

internal static class AlbumGroupedPlaybackOrder
{
    public static string AlbumArtistKey(Song s) =>
        !string.IsNullOrWhiteSpace(s.AlbumArtist) ? s.AlbumArtist : s.Artist ?? string.Empty;

    public static List<Song> BuildOrderedTracks(IEnumerable<Song> allTracks, Func<Song, bool> filter)
    {
        var filtered = allTracks.Where(t => t != null && filter(t)).ToList();
        if (filtered.Count == 0)
            return new List<Song>();

        var groups = filtered
            .GroupBy(t => (Album: t.Album ?? string.Empty, Key: AlbumArtistKey(t)))
            .OrderBy(g => g.Key.Album, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Key.Key, StringComparer.OrdinalIgnoreCase);

        var result = new List<Song>(filtered.Count);
        foreach (var g in groups)
            result.AddRange(AlbumTrackOrder.SortByAlbumSequence(g));

        return result;
    }
}
