using System;
using System.Collections.Generic;
using System.Linq;

namespace musicApp.Helpers;

internal static class GenrePlaybackOrder
{
    private static string AlbumArtistKey(Song s) =>
        !string.IsNullOrWhiteSpace(s.AlbumArtist) ? s.AlbumArtist : s.Artist ?? string.Empty;

    public static List<Song> BuildOrderedGenreTracks(IEnumerable<Song> allTracks, string genre)
    {
        if (string.IsNullOrWhiteSpace(genre))
            return new List<Song>();

        var forGenre = allTracks
            .Where(t => t != null && string.Equals(t.Genre, genre, StringComparison.Ordinal))
            .ToList();

        if (forGenre.Count == 0)
            return new List<Song>();

        var groups = forGenre
            .GroupBy(t => (Album: t.Album ?? string.Empty, Key: AlbumArtistKey(t)))
            .OrderBy(g => g.Key.Album, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Key.Key, StringComparer.OrdinalIgnoreCase);

        var result = new List<Song>(forGenre.Count);
        foreach (var g in groups)
            result.AddRange(AlbumTrackOrder.SortByAlbumSequence(g));

        return result;
    }
}
