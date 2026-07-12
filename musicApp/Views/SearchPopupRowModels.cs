using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace musicApp.Views;

public sealed class AlbumRowViewModel : INotifyPropertyChanged
{
    private ImageSource? _albumArtSource;

    public AlbumRowViewModel(AlbumSearchItem album) => Album = album;

    public AlbumSearchItem Album { get; }
    public string AlbumTitle => Album.AlbumTitle;
    public string Artist => Album.Artist;

    public ImageSource? AlbumArtSource
    {
        get => _albumArtSource;
        set { _albumArtSource = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class SongRowViewModel : INotifyPropertyChanged
{
    private ImageSource? _albumArtSource;
    private bool _isNowPlaying;
    private bool _isSelected;
    private bool _isUserQueued;

    public SongRowViewModel(Song song)
    {
        Song = song;
        _isUserQueued = song?.IsUserQueued ?? false;
    }

    public Song Song { get; }
    public string Title => Song.Title;
    public string Artist => Song.Artist;
    public string Album => Song.Album ?? "";
    public string Duration => Song.Duration ?? "";
    public string ArtistAlbum
    {
        get
        {
            bool hasArtist = !string.IsNullOrWhiteSpace(Song.Artist);
            bool hasAlbum = !string.IsNullOrWhiteSpace(Song.Album);
            if (hasArtist && hasAlbum)
                return $"{Song.Artist} - {Song.Album}";
            if (hasArtist)
                return Song.Artist;
            if (hasAlbum)
                return Song.Album ?? "";
            return "";
        }
    }

    public bool IsNowPlaying
    {
        get => _isNowPlaying;
        set { _isNowPlaying = value; OnPropertyChanged(); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public bool IsUserQueued
    {
        get => _isUserQueued;
        set { _isUserQueued = value; OnPropertyChanged(); }
    }

    public ImageSource? AlbumArtSource
    {
        get => _albumArtSource;
        set { _albumArtSource = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ArtistRowViewModel : INotifyPropertyChanged
{
    private ImageSource? _albumArtSource;

    public ArtistRowViewModel(ArtistSearchItem artist) => Artist = artist;

    public ArtistSearchItem Artist { get; }
    public string Name => Artist.Name;
    public string Subtitle => Artist.Subtitle;

    public Song? RepresentativeTrack => Artist.RepresentativeTrack;

    public ImageSource? AlbumArtSource
    {
        get => _albumArtSource;
        set { _albumArtSource = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
