using System;
using System.Collections.Generic;
using System.Linq;

namespace musicApp.Helpers;

internal static class RecentlyAddedPlaybackOrder
{
    public static List<Song> BuildOrderedTracks(IEnumerable<Song> songsEnumerable)
    {
        var songs = songsEnumerable.ToList();
        if (songs.Count == 0)
            return new List<Song>();

        var orderedGroups = songs
            .Where(t => t != null && !string.IsNullOrWhiteSpace(t.Album) && t.Album != "Unknown Album")
            .GroupBy(t =>
            {
                var albumArtist = !string.IsNullOrWhiteSpace(t.AlbumArtist)
                    ? t.AlbumArtist
                    : t.Artist ?? string.Empty;
                return (Album: t.Album ?? string.Empty, Artist: albumArtist);
            })
            .Select(g =>
            {
                var maxAdded = g.Max(t => t.DateAdded).Date;
                return (g.Key.Album, g.Key.Artist, maxAdded, g);
            })
            .OrderByDescending(x => x.maxAdded)
            .ThenBy(x => x.Album, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Artist, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new List<Song>(songs.Count);
        foreach (var (_, _, _, g) in orderedGroups)
            result.AddRange(AlbumTrackOrder.SortByAlbumSequence(g));

        return result;
    }
}
