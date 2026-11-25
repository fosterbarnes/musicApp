using System;
using System.Collections.Generic;
using System.Linq;

namespace musicApp.Helpers;

internal static class AlbumTrackOrder
{
    public static int ParseDiscNumber(string? disc)
    {
        if (string.IsNullOrWhiteSpace(disc))
            return 0;

        var s = disc.Trim();
        int slash = s.IndexOf('/');
        if (slash >= 0)
            s = s[..slash].Trim();

        if (int.TryParse(s, out int v))
            return v;

        int i = 0;
        while (i < s.Length && !char.IsDigit(s[i]))
            i++;
        int j = i;
        while (j < s.Length && char.IsDigit(s[j]))
            j++;
        if (j > i && int.TryParse(s.AsSpan(i, j - i), out v))
            return v;

        return 0;
    }

    public static List<Song> SortByAlbumSequence(IEnumerable<Song> tracks)
    {
        var list = tracks.ToList();
        if (list.Count <= 1)
            return list;

        int maxDisc = 0;
        foreach (var t in list)
        {
            int d = ParseDiscNumber(t.DiscNumber);
            if (d > maxDisc)
                maxDisc = d;
        }

        bool mergeLooseDisc = maxDisc <= 1;

        return list
            .OrderBy(s =>
            {
                int d = ParseDiscNumber(s.DiscNumber);
                if (d == 0 && mergeLooseDisc)
                    return 1;
                return d;
            })
            .ThenBy(s => s.TrackNumber > 0 ? s.TrackNumber : int.MaxValue)
            .ThenBy(s => s.Title ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.FilePath ?? "", StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

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

/// <summary>
/// Recently Added order, shared by the album grid and its play order so both always list the same thing.
/// </summary>
internal static class RecentlyAddedPlaybackOrder
{
    public sealed record Group(List<Song> Tracks, DateTime LastAdded, bool IsSingleSong);

    public static bool HasAlbum(Song t) =>
        !string.IsNullOrWhiteSpace(t.Album) && !string.Equals(t.Album, "Unknown Album", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Newest first by full Date Added time (not just the day). One group per album (album + album artist, case
    /// ignored), and one per song that has no album, so untagged songs still show up.
    /// </summary>
    public static List<Group> BuildGroups(IEnumerable<Song> songs) =>
        songs
            .Where(t => t != null)
            .GroupBy(t => HasAlbum(t) ? t.Album + "\u001F" + AlbumGroupedPlaybackOrder.AlbumArtistKey(t) : "\u001E" + t.FilePath,
                StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var tracks = g.ToList();
                return new Group(tracks, tracks.Max(t => t.DateAdded), !HasAlbum(tracks[0]));
            })
            .OrderByDescending(g => g.LastAdded)
            .ToList();

    public static List<Song> BuildOrderedTracks(IEnumerable<Song> songs) =>
        BuildGroups(songs).SelectMany(g => AlbumTrackOrder.SortByAlbumSequence(g.Tracks)).ToList();
}
