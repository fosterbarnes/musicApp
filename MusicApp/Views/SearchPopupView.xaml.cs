using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using MusicApp;
using MusicApp.Helpers;

namespace MusicApp.Views;

public partial class SearchPopupView : UserControl
{
    private const double MinSearchPopupHeight = 160;
    private const double DefaultSearchPopupHeight = 575;
    private const double MaxSearchPopupHeight = 850;
    private Song? _contextMenuSong;
    private int _heightAdjustGeneration;

    public SearchPopupView()
    {
        InitializeComponent();
        PopupBorder.MinHeight = MinSearchPopupHeight;
        PopupBorder.Height = DefaultSearchPopupHeight;
        PopupBorder.MaxHeight = MaxSearchPopupHeight;
    }

    public SearchResults? Results
    {
        get => (SearchResults?)GetValue(ResultsProperty);
        set => SetValue(ResultsProperty, value);
    }

    public static readonly DependencyProperty ResultsProperty =
        DependencyProperty.Register(nameof(Results), typeof(SearchResults), typeof(SearchPopupView),
            new PropertyMetadata(null, OnResultsChanged));

    private static void OnResultsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SearchPopupView view || e.NewValue is not SearchResults results)
            return;

        // Show list and text immediately (no images yet)
        var albumRows = new ObservableCollection<AlbumRowViewModel>(
            results.Albums.Select(a => new AlbumRowViewModel(a)));
        var artistRows = new ObservableCollection<ArtistRowViewModel>(
            results.Artists.Select(a => new ArtistRowViewModel(a)));
        var songRows = new ObservableCollection<SongRowViewModel>(
            results.Songs.Select(s => new SongRowViewModel(s)));

        view.AlbumsList.ItemsSource = albumRows;
        view.ArtistsList.ItemsSource = artistRows;
        view.SongsList.ItemsSource = songRows;

        bool any = results.Albums.Count > 0 || results.Artists.Count > 0 || results.Songs.Count > 0;
        view.NoResultsText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;

        // Hide sections with no results; show only sections that have results.
        view.AlbumsSection.Visibility = results.Albums.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        view.ArtistsSection.Visibility = results.Artists.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        view.SongsSection.Visibility = results.Songs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Order sections: only include sections that have results, in SectionOrder (Songs first unless exact match for artist/album).
        view.SectionsPanel.Children.Remove(view.AlbumsSection);
        view.SectionsPanel.Children.Remove(view.ArtistsSection);
        view.SectionsPanel.Children.Remove(view.SongsSection);
        int index = 0;
        if (results.SectionOrder?.Count >= 3)
        {
            foreach (var section in results.SectionOrder)
            {
                if (section == SearchSection.Albums && results.Albums.Count > 0 ||
                    section == SearchSection.Artists && results.Artists.Count > 0 ||
                    section == SearchSection.Songs && results.Songs.Count > 0)
                {
                    view.SectionsPanel.Children.Insert(index++, view.SectionPanelFor(section));
                }
            }
        }
        else
        {
            if (results.Albums.Count > 0) view.SectionsPanel.Children.Insert(index++, view.AlbumsSection);
            if (results.Artists.Count > 0) view.SectionsPanel.Children.Insert(index++, view.ArtistsSection);
            if (results.Songs.Count > 0) view.SectionsPanel.Children.Insert(index++, view.SongsSection);
        }

        // Height must run after popup is open + ItemsControl has laid out; see ScheduleAdjustHeightForSearch.
        view.ScheduleAdjustHeightForSearch();

        // Populate images when ready (band-aid: load async after list is shown).
        // TODO: Implement a better solution (e.g. dedicated image cache, virtualization-friendly loading, cancel on new search).
        _ = view.LoadAlbumArtWhenReadyAsync(artistRows, albumRows, songRows);
    }

    private StackPanel SectionPanelFor(SearchSection section) => section switch
    {
        SearchSection.Albums => AlbumsSection,
        SearchSection.Artists => ArtistsSection,
        SearchSection.Songs => SongsSection,
        _ => SongsSection
    };

    public void RefreshHeightForSearch() => ScheduleAdjustHeightForSearch();

    private void ScheduleAdjustHeightForSearch()
    {
        int gen = ++_heightAdjustGeneration;
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (gen != _heightAdjustGeneration)
                return;
            AdjustPopupHeightToResults();
        }), DispatcherPriority.ContextIdle);
    }

    private void AdjustPopupHeightToResults()
    {
        PopupBorder.Height = DefaultSearchPopupHeight;
        SectionsPanel.InvalidateMeasure();
        SearchScrollViewer.InvalidateMeasure();
        UpdateLayout();
        SearchScrollViewer.UpdateLayout();

        double viewportHeight = SearchScrollViewer.ViewportHeight;
        double actualBorderHeight = PopupBorder.ActualHeight;
        if (viewportHeight <= 0 || actualBorderHeight <= 0)
        {
            PopupBorder.Height = DefaultSearchPopupHeight;
            return;
        }

        double overhead = actualBorderHeight - viewportHeight;

        double measureWidth = SearchScrollViewer.ViewportWidth;
        if (measureWidth <= 0)
            measureWidth = Math.Max(PopupBorder.ActualWidth - 24, PopupBorder.MinWidth - 24);

        SectionsPanel.Measure(new Size(measureWidth, double.PositiveInfinity));
        double contentHeight = Math.Max(SectionsPanel.DesiredSize.Height, SearchScrollViewer.ExtentHeight);

        double desiredTotalHeight = contentHeight + overhead;

        PopupBorder.Height = desiredTotalHeight >= DefaultSearchPopupHeight
            ? DefaultSearchPopupHeight
            : Math.Max(MinSearchPopupHeight, desiredTotalHeight);
    }

    private async System.Threading.Tasks.Task LoadAlbumArtWhenReadyAsync(
        ObservableCollection<ArtistRowViewModel> artistRows,
        ObservableCollection<AlbumRowViewModel> albumRows,
        ObservableCollection<SongRowViewModel> songRows)
    {
        foreach (var row in artistRows)
        {
            var rep = row.RepresentativeTrack;
            if (rep == null) continue;

            var img = await System.Threading.Tasks.Task.Run(() => AlbumArtThumbnailHelper.LoadForTrack(rep))
                .ConfigureAwait(false);
            if (img != null)
                await Dispatcher.InvokeAsync(() => row.AlbumArtSource = img);
        }

        foreach (var row in albumRows)
        {
            var song = row.Album.Songs.FirstOrDefault();
            if (song == null) continue;
            var img = await System.Threading.Tasks.Task.Run(() => AlbumArtThumbnailHelper.LoadForTrack(song))
                .ConfigureAwait(false);
            if (img != null)
                await Dispatcher.InvokeAsync(() => row.AlbumArtSource = img);
        }

        foreach (var row in songRows)
        {
            var img = await System.Threading.Tasks.Task.Run(() => AlbumArtThumbnailHelper.LoadForTrack(row.Song))
                .ConfigureAwait(false);
            if (img != null)
                await Dispatcher.InvokeAsync(() => row.AlbumArtSource = img);
        }
    }

    public event System.EventHandler<Song>? SongSelected;
    public event System.EventHandler<ArtistSearchItem>? ArtistSelected;
    public event System.EventHandler<AlbumSearchItem>? AlbumSelected;

    public event EventHandler<Song>? PlayNextRequested;
    public event EventHandler<Song>? AddToQueueRequested;
    public event EventHandler<(Song track, Playlist playlist)>? AddTrackToPlaylistRequested;
    public event EventHandler<Song>? CreateNewPlaylistWithTrackRequested;
    public event EventHandler<Song>? InfoRequested;
    public event EventHandler<Song>? ShowInArtistsRequested;
    public event EventHandler<Song>? ShowInSongsRequested;
    public event EventHandler<Song>? ShowInAlbumsRequested;
    public event EventHandler<Song>? ShowInExplorerRequested;
    public event EventHandler<Song>? RemoveFromLibraryRequested;
    public event EventHandler<Song>? DeleteRequested;

    private void SongItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is SongRowViewModel row)
        {
            SongSelected?.Invoke(this, row.Song);
            e.Handled = true;
        }
    }

    private void ArtistItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is ArtistRowViewModel row)
        {
            ArtistSelected?.Invoke(this, row.Artist);
            e.Handled = true;
        }
    }

    private void AlbumItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is AlbumRowViewModel row)
        {
            AlbumSelected?.Invoke(this, row.Album);
            e.Handled = true;
        }
    }

    private void SearchContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        _contextMenuSong = null;
        if (sender is not ContextMenu menu || menu.PlacementTarget is not FrameworkElement target)
            return;
        if (target.DataContext is SongRowViewModel songRow)
            _contextMenuSong = songRow.Song;
        else if (target.DataContext is AlbumRowViewModel albumRow && albumRow.Album.Songs.Count > 0)
            _contextMenuSong = albumRow.Album.Songs[0];

        if (_contextMenuSong == null) return;

        var addToPlaylistItem = FindMenuItemByHeader(menu.Items, "Add to Playlist");
        if (addToPlaylistItem != null)
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            var playlists = mainWindow?.Playlists;
            if (playlists != null)
            {
                while (addToPlaylistItem.Items.Count > 2)
                    addToPlaylistItem.Items.RemoveAt(2);
                foreach (var playlist in playlists)
                {
                    var mi = new MenuItem { Header = playlist.Name, Tag = playlist };
                    mi.Click += SearchContextMenu_PlaylistSubmenuClick;
                    addToPlaylistItem.Items.Add(mi);
                }
            }
        }
    }

    private static MenuItem? FindMenuItemByHeader(ItemCollection items, string header)
    {
        foreach (var item in items)
            if (item is MenuItem mi && mi.Header?.ToString() == header)
                return mi;
        return null;
    }

    private void SearchContextMenu_PlaylistSubmenuClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuSong == null || sender is not MenuItem mi || mi.Tag is not Playlist playlist)
            return;
        AddTrackToPlaylistRequested?.Invoke(this, (_contextMenuSong, playlist));
    }

    private void SearchContextMenu_PlayNextClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) PlayNextRequested?.Invoke(this, _contextMenuSong); }
    private void SearchContextMenu_AddToQueueClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) AddToQueueRequested?.Invoke(this, _contextMenuSong); }
    private void SearchContextMenu_AddToPlaylistClick(object sender, RoutedEventArgs e) { }
    private void SearchContextMenu_NewPlaylistClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) CreateNewPlaylistWithTrackRequested?.Invoke(this, _contextMenuSong); }
    private void SearchContextMenu_InfoClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) InfoRequested?.Invoke(this, _contextMenuSong); }
    private void SearchContextMenu_ShowInArtistsClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) ShowInArtistsRequested?.Invoke(this, _contextMenuSong); }
    private void SearchContextMenu_ShowInSongsClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) ShowInSongsRequested?.Invoke(this, _contextMenuSong); }
    private void SearchContextMenu_ShowInAlbumsClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) ShowInAlbumsRequested?.Invoke(this, _contextMenuSong); }
    private void SearchContextMenu_ShowInExplorerClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) ShowInExplorerRequested?.Invoke(this, _contextMenuSong); }
    private void SearchContextMenu_RemoveFromLibraryClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) RemoveFromLibraryRequested?.Invoke(this, _contextMenuSong); }
    private void SearchContextMenu_DeleteClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) DeleteRequested?.Invoke(this, _contextMenuSong); }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var currentHeight = !double.IsNaN(PopupBorder.Height) ? PopupBorder.Height : PopupBorder.ActualHeight;
        if (currentHeight <= 0)
            currentHeight = MinSearchPopupHeight;

        var maxHeight = GetAvailableMaxHeight();
        var newHeight = Math.Clamp(currentHeight + e.VerticalChange, MinSearchPopupHeight, maxHeight);
        PopupBorder.Height = newHeight;
    }

    private double GetAvailableMaxHeight()
    {
        var hostWindow = Window.GetWindow(this);
        if (hostWindow == null || hostWindow.ActualHeight <= 0)
            return MaxSearchPopupHeight;

        // Leave space so popup does not extend beyond the app window.
        var windowLimitedMax = hostWindow.ActualHeight - 140;
        return Math.Clamp(windowLimitedMax, MinSearchPopupHeight, MaxSearchPopupHeight);
    }
}
