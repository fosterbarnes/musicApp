using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace musicApp.Views;

public partial class CompactSongRow : UserControl
{
    public CompactSongRow() => InitializeComponent();

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(CompactSongRow),
            new PropertyMetadata(""));

    public string Artist
    {
        get => (string)GetValue(ArtistProperty);
        set => SetValue(ArtistProperty, value);
    }

    public static readonly DependencyProperty ArtistProperty =
        DependencyProperty.Register(nameof(Artist), typeof(string), typeof(CompactSongRow),
            new PropertyMetadata(""));

    public ImageSource? AlbumArtSource
    {
        get => (ImageSource?)GetValue(AlbumArtSourceProperty);
        set => SetValue(AlbumArtSourceProperty, value);
    }

    public static readonly DependencyProperty AlbumArtSourceProperty =
        DependencyProperty.Register(nameof(AlbumArtSource), typeof(ImageSource), typeof(CompactSongRow),
            new PropertyMetadata(null));

    public bool IsNowPlaying
    {
        get => (bool)GetValue(IsNowPlayingProperty);
        set => SetValue(IsNowPlayingProperty, value);
    }

    public static readonly DependencyProperty IsNowPlayingProperty =
        DependencyProperty.Register(nameof(IsNowPlaying), typeof(bool), typeof(CompactSongRow),
            new PropertyMetadata(false));

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(CompactSongRow),
            new PropertyMetadata(false));
}
