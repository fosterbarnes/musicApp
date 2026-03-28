using System;
using System.Collections.Generic;
using System.Linq;

namespace musicApp.Helpers;

internal static class ArtistPlaybackOrder
{
    private static string AlbumArtistKey(Song s) =>
        !string.IsNullOrWhiteSpace(s.AlbumArtist) ? s.AlbumArtist : s.Artist ?? string.Empty;

    public static List<Song> BuildOrderedArtistTracks(IEnumerable<Song> allTracks, string artist)
    {
        if (string.IsNullOrWhiteSpace(artist))
            return new List<Song>();

        var forArtist = allTracks
            .Where(t => t != null && string.Equals(t.Artist, artist, StringComparison.Ordinal))
            .ToList();

        if (forArtist.Count == 0)
            return new List<Song>();

        var groups = forArtist
            .GroupBy(t => (Album: t.Album ?? string.Empty, Key: AlbumArtistKey(t)))
            .OrderBy(g => g.Key.Album, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Key.Key, StringComparer.OrdinalIgnoreCase);

        var result = new List<Song>(forArtist.Count);
        foreach (var g in groups)
            result.AddRange(AlbumTrackOrder.SortByAlbumSequence(g));

        return result;
    }

    public static int IndexOfTrackInOrderedList(IReadOnlyList<Song> ordered, Song selected)
    {
        if (ordered.Count == 0 || selected == null)
            return -1;

        if (!string.IsNullOrWhiteSpace(selected.FilePath))
        {
            for (int i = 0; i < ordered.Count; i++)
            {
                var t = ordered[i];
                if (t != null && string.Equals(t.FilePath, selected.FilePath, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }

        for (int i = 0; i < ordered.Count; i++)
        {
            if (ReferenceEquals(ordered[i], selected))
                return i;
        }

        for (int i = 0; i < ordered.Count; i++)
        {
            var t = ordered[i];
            if (t == null) continue;
            if (string.Equals(t.Title, selected.Title, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(t.Artist, selected.Artist, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(t.Album, selected.Album, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }
}
