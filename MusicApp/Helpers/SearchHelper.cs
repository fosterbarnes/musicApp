using System;
using System.Collections.Generic;
using System.Linq;
using MusicApp;

namespace MusicApp.Helpers;

public static class SearchHelper
{
    private const int SongLimit = 50;
    private const int ArtistLimit = 5;
    private const int AlbumLimit = 5;

    private static readonly StringComparison IgnoreCase = StringComparison.OrdinalIgnoreCase;

    /// <summary>Run search and return sectioned results for Albums, Artists, Songs. Uses exact substring match (case-insensitive).</summary>
    public static SearchResults Run(string query, IEnumerable<Song> allTracks)
    {
        var tracks = allTracks.ToList();
        if (string.IsNullOrWhiteSpace(query) || tracks.Count == 0)
            return new SearchResults();

        var q = query.Trim();
        var results = new SearchResults();

        // Songs: only hits where the query is in the song's own title, artist, or genre (not album — those appear in Albums section only)
        var matchedSongs = tracks
            .Where(s => (s.Title?.Contains(q, IgnoreCase) == true) ||
                        (s.Artist?.Contains(q, IgnoreCase) == true) ||
                        (s.Genre?.Contains(q, IgnoreCase) == true))
            .Take(SongLimit)
            .ToList();
        results.Songs = matchedSongs;

        // Artists: distinct names that contain the query, with album/song counts
        var distinctArtists = tracks
            .Where(t => !string.IsNullOrWhiteSpace(t.Artist) && t.Artist.Contains(q, IgnoreCase))
            .Select(t => t.Artist)
            .Distinct()
            .Take(ArtistLimit);
        foreach (var name in distinctArtists)
        {
            var artistTracks = tracks.Where(t => t.Artist == name).ToList();
            results.Artists.Add(new ArtistSearchItem
            {
                Name = name,
                AlbumCount = artistTracks.Select(t => (t.Album ?? "", t.Artist ?? "")).Distinct().Count(),
                SongCount = artistTracks.Count
            });
        }

        // Albums: distinct (Album, Artist) where Album or Artist contains the query
        var albumKeys = tracks
            .Where(t => !string.IsNullOrWhiteSpace(t.Album) && t.Album != "Unknown Album" &&
                       ((t.Album?.Contains(q, IgnoreCase) == true) || (t.Artist?.Contains(q, IgnoreCase) == true)))
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
}
