using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using musicApp.Helpers;

namespace musicApp.Views;

public sealed class AlbumGridItem : INotifyPropertyChanged
{
    private string? _artKey;

    public AlbumGridItem(string albumTitle, string artist, Song representativeTrack)
    {
        AlbumTitle = albumTitle;
        Artist = artist;
        RepresentativeTrack = representativeTrack;
    }

    /// <summary>Decode size of grid art, set by the albums view when the tile size changes.</summary>
    public static int ArtPx { get; set; } = 158;

    public string AlbumTitle { get; }
    public string Artist { get; }
    public Song RepresentativeTrack { get; }

    /// <summary>Recently Added tile for a song with no album: titled with the song, holds only that song.</summary>
    public bool IsSingleSong { get; init; }

    /// <summary>This album's <see cref="AlbumGridArtCache"/> key (its disk thumbnail path).</summary>
    public string ArtKey => _artKey ??= AlbumArtCacheManager.GetCachedPathForTrack(RepresentativeTrack);

    /// <summary>
    /// Read from the cache when a tile binds, decoding on a miss, so a tile never shows without its art
    /// (prefetch keeps misses rare). The item holds no bitmap itself.
    /// </summary>
    public ImageSource? AlbumArtSource => AlbumGridArtCache.GetOrLoad(ArtKey, RepresentativeTrack, ArtPx);

    /// <summary>Re-reads the art: after a tile size change or an art change for this album.</summary>
    public void NotifyArtChanged() => OnPropertyChanged(nameof(AlbumArtSource));

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One grid row of up to tiles-per-row albums (the grid is a virtualized list of rows).</summary>
public sealed class AlbumTileRow
{
    public AlbumTileRow(List<AlbumGridItem> albums) => Albums = albums;
    public List<AlbumGridItem> Albums { get; }
}

public sealed class AlbumSectionHeaderItem
{
    public AlbumSectionHeaderItem(string title) => Title = title;
    public string Title { get; }
}

public sealed class AlbumFlyoutItem : INotifyPropertyChanged
{
    private ImageSource? _albumArtSource;

    public string AlbumTitle { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Genre { get; set; } = "";
    public string Year { get; set; } = "";
    public string AlbumMetadata { get; set; } = "";
    public List<Song> Tracks { get; set; } = new();
    public List<Song> TracksColumn1 { get; set; } = new();
    public List<Song> TracksColumn2 { get; set; } = new();

    public ImageSource? AlbumArtSource
    {
        get => _albumArtSource;
        set { _albumArtSource = value; OnPropertyChanged(); }
    }

    /// <summary>Re-reads every binding after the plain properties above were patched on an open flyout.</summary>
    public void NotifyAllChanged() => OnPropertyChanged(string.Empty);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
