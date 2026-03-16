using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace MusicApp.Views;

/// <summary>Display item for one album in the album grid. AlbumArtSource is populated async via AlbumArtThumbnailHelper.LoadForTrack(RepresentativeTrack, 158).</summary>
public sealed class AlbumGridItem : INotifyPropertyChanged
{
    private BitmapImage? _albumArtSource;

    public AlbumGridItem(string albumTitle, string artist, Song representativeTrack)
    {
        AlbumTitle = albumTitle;
        Artist = artist;
        RepresentativeTrack = representativeTrack;
    }

    public string AlbumTitle { get; }
    public string Artist { get; }
    public Song RepresentativeTrack { get; }

    public BitmapImage? AlbumArtSource
    {
        get => _albumArtSource;
        set { _albumArtSource = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
