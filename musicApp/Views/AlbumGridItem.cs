using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace musicApp.Views;

public sealed class AlbumGridItem : INotifyPropertyChanged
{
    private ImageSource? _albumArtSource;

    public AlbumGridItem(string albumTitle, string artist, Song representativeTrack, ImageSource? initialArt = null)
    {
        AlbumTitle = albumTitle;
        Artist = artist;
        RepresentativeTrack = representativeTrack;
        _albumArtSource = initialArt;
    }

    public string AlbumTitle { get; }
    public string Artist { get; }
    public Song RepresentativeTrack { get; }

    public ImageSource? AlbumArtSource
    {
        get => _albumArtSource;
        set { _albumArtSource = value; OnPropertyChanged(); }
    }

    public void PrebindSetAlbumArt(ImageSource? img)
    {
        if (img == null)
            return;
        _albumArtSource = img;
    }

    public void NotifyAlbumArtBound() => OnPropertyChanged(nameof(AlbumArtSource));

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
