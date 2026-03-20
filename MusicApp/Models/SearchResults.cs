namespace MusicApp;

/// <summary>Display order for search result sections. Songs first unless exact match for artist/album.</summary>
public enum SearchSection
{
    Songs,
    Artists,
    Albums
}

/// <summary>Sectioned search results for popup: albums, artists, songs.</summary>
public class SearchResults
{
    public List<AlbumSearchItem> Albums { get; set; } = new();
    public List<ArtistSearchItem> Artists { get; set; } = new();
    public List<Song> Songs { get; set; } = new();
    /// <summary>Order to show sections: first element is top of popup. Songs &gt; Artist &gt; Album unless exact match for artist/album.</summary>
    public List<SearchSection> SectionOrder { get; set; } = new();
}

public class AlbumSearchItem
{
    public string AlbumTitle { get; set; } = "";
    public string Artist { get; set; } = "";
    public string AlbumArtPath { get; set; } = "";
    /// <summary>Songs on this album (for navigation / display).</summary>
    public List<Song> Songs { get; set; } = new();
}

public class ArtistSearchItem
{
    public string Name { get; set; } = "";
    public int AlbumCount { get; set; }
    public int SongCount { get; set; }
    // Used by the UI to display album artwork for the artist (e.g. oldest album).
    public Song? RepresentativeTrack { get; set; }
    public string Subtitle => $"{AlbumCount} albums, {SongCount} songs";
}
