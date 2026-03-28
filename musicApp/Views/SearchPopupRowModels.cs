using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace musicApp.Views;

/// <summary>Display row for album in search popup. Text shown immediately; AlbumArtSource populated async.</summary>
public sealed class AlbumRowViewModel : INotifyPropertyChanged
{
    private BitmapImage? _albumArtSource;

    public AlbumRowViewModel(AlbumSearchItem album) => Album = album;

    public AlbumSearchItem Album { get; }
    public string AlbumTitle => Album.AlbumTitle;
    public string Artist => Album.Artist;

    public BitmapImage? AlbumArtSource
    {
        get => _albumArtSource;
        set { _albumArtSource = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Display row for song in search popup. Text shown immediately; AlbumArtSource populated async.</summary>
public sealed class SongRowViewModel : INotifyPropertyChanged
{
    private BitmapImage? _albumArtSource;
    private bool _isNowPlaying;
    private bool _isSelected;

    public SongRowViewModel(Song song) => Song = song;

    public Song Song { get; }
    public string Title => Song.Title;
    public string Artist => Song.Artist;

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

    public BitmapImage? AlbumArtSource
    {
        get => _albumArtSource;
        set { _albumArtSource = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Display row for artist in search popup. AlbumArtSource populated async.</summary>
public sealed class ArtistRowViewModel : INotifyPropertyChanged
{
    private BitmapImage? _albumArtSource;

    public ArtistRowViewModel(ArtistSearchItem artist) => Artist = artist;

    public ArtistSearchItem Artist { get; }
    public string Name => Artist.Name;
    public string Subtitle => Artist.Subtitle;

    public Song? RepresentativeTrack => Artist.RepresentativeTrack;

    public BitmapImage? AlbumArtSource
    {
        get => _albumArtSource;
        set { _albumArtSource = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
