using System;
using System.Collections.Generic;
using System.Linq;
using musicApp;

namespace musicApp.Helpers;

public static class SearchHelper
{
    private const int SongLimit = 50;
    private const int ArtistLimit = 5;
    private const int AlbumLimit = 5;

    private static readonly StringComparison IgnoreCase = StringComparison.OrdinalIgnoreCase;

    /// <summary>Run search and return sectioned results for Albums, Artists, Songs. Uses case-insensitive word-prefix matching.</summary>
    public static SearchResults Run(string query, IEnumerable<Song> allTracks)
    {
        var tracks = allTracks.ToList();
        if (string.IsNullOrWhiteSpace(query) || tracks.Count == 0)
            return new SearchResults();

        var q = query.Trim();
        var queryWords = q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (queryWords.Length == 0)
            return new SearchResults();
        var results = new SearchResults();

        // Songs: only hits where query words match the start of one or more words
        // in the song's own title, artist, or genre (not album - those appear in Albums section only).
        var matchedSongs = tracks
            .Where(s => MatchesQueryWords(s.Title, queryWords) ||
                        MatchesQueryWords(s.Artist, queryWords) ||
                        MatchesQueryWords(s.Genre, queryWords))
            .Take(SongLimit)
            .ToList();
        results.Songs = matchedSongs;

        // Artists: distinct names where query words match start of words, with album/song counts.
        var distinctArtists = tracks
            .Where(t => !string.IsNullOrWhiteSpace(t.Artist) && MatchesQueryWords(t.Artist, queryWords))
            .Select(t => t.Artist)
            .Distinct()
            .Take(ArtistLimit);
        foreach (var name in distinctArtists)
        {
            var artistTracks = tracks.Where(t => t.Artist == name).ToList();
            var representative = GetArtistOldestAlbumRepresentativeTrack(artistTracks);
            results.Artists.Add(new ArtistSearchItem
            {
                Name = name,
                AlbumCount = artistTracks.Select(t => (t.Album ?? "", t.Artist ?? "")).Distinct().Count(),
                SongCount = artistTracks.Count,
                RepresentativeTrack = representative
            });
        }

        // Albums: distinct (Album, Artist) where Album or Artist matches query word prefixes.
        var albumKeys = tracks
            .Where(t => !string.IsNullOrWhiteSpace(t.Album) && t.Album != "Unknown Album" &&
                       (MatchesQueryWords(t.Album, queryWords) || MatchesQueryWords(t.Artist, queryWords)))
            .GroupBy(t => (Album: t.Album, Artist: t.Artist ?? ""))
            .Select(g => new { Key = g.Key, Songs = g.ToList() })
            .Take(AlbumLimit);
        foreach (var bucket in albumKeys)
        {
            var first = bucket.Songs.First();
            results.Albums.Add(new AlbumSearchItem
            {
                AlbumTitle = bucket.Key.Album,
                Artist = bucket.Key.Artist,
                AlbumArtPath = first.AlbumArtPath ?? "",
                Songs = bucket.Songs
            });
        }

        // Section order: prioritize Songs unless exact match for artist/album. If song, album, and artist all match same name: Songs > Artist > Album.
        bool exactSong = results.Songs.Any(s => string.Equals(s.Title?.Trim(), q, IgnoreCase));
        bool exactArtist = results.Artists.Any(a => string.Equals(a.Name.Trim(), q, IgnoreCase));
        bool exactAlbum = results.Albums.Any(a => string.Equals(a.AlbumTitle?.Trim(), q, IgnoreCase));

        if (exactSong && exactArtist && exactAlbum)
            results.SectionOrder = new List<SearchSection> { SearchSection.Songs, SearchSection.Artists, SearchSection.Albums };
        else if (exactArtist)
            results.SectionOrder = new List<SearchSection> { SearchSection.Artists, SearchSection.Songs, SearchSection.Albums };
        else if (exactAlbum)
            results.SectionOrder = new List<SearchSection> { SearchSection.Albums, SearchSection.Songs, SearchSection.Artists };
        else
            results.SectionOrder = new List<SearchSection> { SearchSection.Songs, SearchSection.Artists, SearchSection.Albums };

        return results;
    }

    private static bool MatchesQueryWords(string? text, IReadOnlyList<string> queryWords)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        for (var i = 0; i < queryWords.Count; i++)
        {
            if (!ContainsWordStartingWith(text, queryWords[i]))
                return false;
        }

        return true;
    }

    private static bool ContainsWordStartingWith(string text, string queryWord)
    {
        if (queryWord.Length == 0)
            return true;

        var span = text.AsSpan();
        var querySpan = queryWord.AsSpan();
        var index = 0;

        while (index < span.Length)
        {
            while (index < span.Length && !char.IsLetterOrDigit(span[index]))
                index++;

            var start = index;

            while (index < span.Length && char.IsLetterOrDigit(span[index]))
                index++;

            if (start < index && span[start..index].StartsWith(querySpan, IgnoreCase))
                return true;
        }

        return false;
    }

    private static Song? GetArtistOldestAlbumRepresentativeTrack(List<Song> artistTracks)
    {
        if (artistTracks == null || artistTracks.Count == 0)
            return null;

        // Prefer real album names; fall back to everything if we can't find any.
        var preferredTracks = artistTracks
            .Where(t => !string.IsNullOrWhiteSpace(t.Album) &&
                        !string.Equals(t.Album, "Unknown Album", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var candidates = preferredTracks.Count > 0 ? preferredTracks : artistTracks;

        static DateTime GetSongDate(Song s)
        {
            if (s.ReleaseDate.HasValue)
                return s.ReleaseDate.Value.Date;
            if (s.Year > 0)
                return new DateTime(s.Year, 1, 1);
            return DateTime.MaxValue;
        }

        // Group by album name (case-insensitive), then pick the album whose oldest track date is earliest.
        var albumGroups = candidates
            .Where(t => !string.IsNullOrWhiteSpace(t.Album))
            .GroupBy(t => t.Album, StringComparer.OrdinalIgnoreCase);

        Song? best = null;
        DateTime bestDate = DateTime.MaxValue;

        foreach (var g in albumGroups)
        {
            var oldestTrack = g.OrderBy(GetSongDate).FirstOrDefault();
            if (oldestTrack == null)
                continue;

            var date = GetSongDate(oldestTrack);
            if (date < bestDate)
            {
                bestDate = date;
                best = oldestTrack;
            }
        }

        // If all album groups had unknown dates, fall back to the earliest-dated track in the artist.
        best ??= artistTracks.OrderBy(GetSongDate).FirstOrDefault();
        return best;
    }
}
