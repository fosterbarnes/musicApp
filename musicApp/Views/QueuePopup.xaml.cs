using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using musicApp;
using musicApp.Constants;
using musicApp.Helpers;

namespace musicApp.Views;

public partial class QueuePopupView : UserControl
{
    private Song? _contextMenuSong;
    private int _heightAdjustGeneration;

    public QueuePopupView()
    {
        InitializeComponent();
        PopupBorder.MinHeight = UILayoutConstants.CompactPopupMinHeight;
        PopupBorder.Height = UILayoutConstants.CompactPopupDefaultHeight;
        PopupBorder.MaxHeight = UILayoutConstants.CompactPopupMaxHeight;
    }

    public IEnumerable? QueueTracks
    {
        get => (IEnumerable?)GetValue(QueueTracksProperty);
        set => SetValue(QueueTracksProperty, value);
    }

    public static readonly DependencyProperty QueueTracksProperty =
        DependencyProperty.Register(nameof(QueueTracks), typeof(IEnumerable), typeof(QueuePopupView),
            new PropertyMetadata(null, OnQueueTracksChanged));

    private static void OnQueueTracksChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not QueuePopupView view)
            return;

        Song? previouslySelected = GetSelectedSongFromItemsSource(view);

        var rows = new ObservableCollection<SongRowViewModel>();
        if (e.NewValue is IEnumerable enumerable)
        {
            int i = 0;
            foreach (var item in enumerable)
            {
                if (item is Song song)
                {
                    var vm = new SongRowViewModel(song) { IsNowPlaying = i == 0 };
                    rows.Add(vm);
                    i++;
                }
            }
        }

        view.QueueItemsList.ItemsSource = rows;
        ApplySelectionAfterRebuild(rows, previouslySelected);
        bool any = rows.Count > 0;
        view.EmptyQueueText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        view.ScheduleAdjustHeight();
        _ = view.LoadSongRowArtAsync(rows);
    }

    private static Song? GetSelectedSongFromItemsSource(QueuePopupView view)
    {
        if (view.QueueItemsList.ItemsSource is not IEnumerable en)
            return null;
        foreach (var item in en)
        {
            if (item is SongRowViewModel vm && vm.IsSelected)
                return vm.Song;
        }
        return null;
    }

    private static void ApplySelectionAfterRebuild(ObservableCollection<SongRowViewModel> rows, Song? selectedSong)
    {
        foreach (var r in rows)
            r.IsSelected = false;
        if (selectedSong == null)
            return;
        foreach (var r in rows)
        {
            if (ReferenceEquals(r.Song, selectedSong))
            {
                r.IsSelected = true;
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(selectedSong.FilePath))
            return;
        foreach (var r in rows)
        {
            if (!string.IsNullOrWhiteSpace(r.Song.FilePath) &&
                string.Equals(selectedSong.FilePath, r.Song.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                r.IsSelected = true;
                return;
            }
        }
    }

    public void RefreshHeight() => ScheduleAdjustHeight();

    private void ScheduleAdjustHeight()
    {
        int gen = ++_heightAdjustGeneration;
        CompactPopupHeightHelper.ScheduleAdjust(Dispatcher, gen, () => _heightAdjustGeneration, AdjustPopupHeight);
    }

    private void AdjustPopupHeight()
    {
        CompactPopupHeightHelper.AdjustBorderHeightToContent(
            PopupBorder, QueueScrollViewer, QueueContentPanel, this);
    }

    private async System.Threading.Tasks.Task LoadSongRowArtAsync(ObservableCollection<SongRowViewModel> rows)
    {
        foreach (var row in rows)
        {
            var img = await System.Threading.Tasks.Task.Run(() => AlbumArtThumbnailHelper.LoadForTrack(row.Song))
                .ConfigureAwait(false);
            if (img != null)
                await Dispatcher.InvokeAsync(() => row.AlbumArtSource = img);
        }
    }

    public event System.EventHandler<Song>? SongPlayRequested;

    public event EventHandler? QueueToolbarRemoveRequested;
    public event EventHandler? QueueToolbarMoveUpRequested;
    public event EventHandler? QueueToolbarMoveDownRequested;

    public event EventHandler<Song>? PlayNextRequested;
    public event EventHandler<Song>? AddToQueueRequested;
    public event EventHandler<(Song track, Playlist playlist)>? AddTrackToPlaylistRequested;
    public event EventHandler<Song>? CreateNewPlaylistWithTrackRequested;
    public event EventHandler<Song>? InfoRequested;
    public event EventHandler<Song>? ShowInArtistsRequested;
    public event EventHandler<Song>? ShowInSongsRequested;
    public event EventHandler<Song>? ShowInAlbumsRequested;
    public event EventHandler<Song>? ShowInQueueRequested;
    public event EventHandler<Song>? ShowInExplorerRequested;
    public event EventHandler<Song>? RemoveFromLibraryRequested;
    public event EventHandler<Song>? DeleteRequested;

    public int GetSelectedViewIndex()
    {
        if (QueueItemsList.ItemsSource is not IEnumerable en)
            return -1;
        int i = 0;
        foreach (var item in en)
        {
            if (item is SongRowViewModel vm && vm.IsSelected)
                return i;
            i++;
        }
        return -1;
    }

    private void ApplyRowSelection(SongRowViewModel selected)
    {
        if (QueueItemsList.ItemsSource is not IEnumerable en)
            return;
        foreach (var item in en)
        {
            if (item is SongRowViewModel vm)
                vm.IsSelected = ReferenceEquals(vm, selected);
        }
    }

    private void QueueSongItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is SongRowViewModel row)
        {
            ApplyRowSelection(row);
            e.Handled = true;
        }
    }

    private void QueueSongItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is SongRowViewModel row)
        {
            SongPlayRequested?.Invoke(this, row.Song);
            e.Handled = true;
        }
    }

    private void QueueToolbarRemove_Click(object sender, RoutedEventArgs e) =>
        QueueToolbarRemoveRequested?.Invoke(this, EventArgs.Empty);

    private void QueueToolbarMoveUp_Click(object sender, RoutedEventArgs e) =>
        QueueToolbarMoveUpRequested?.Invoke(this, EventArgs.Empty);

    private void QueueToolbarMoveDown_Click(object sender, RoutedEventArgs e) =>
        QueueToolbarMoveDownRequested?.Invoke(this, EventArgs.Empty);

    private void QueueContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        _contextMenuSong = null;
        if (sender is not ContextMenu menu || menu.PlacementTarget is not DependencyObject placement)
            return;

        if (!TrackContextMenuHelper.TryResolveSong(placement, out var song) || song == null)
            return;

        _contextMenuSong = song;

        var addToPlaylistItem = TrackContextMenuHelper.FindMenuItemByHeader(menu.Items, "Add to Playlist");
        var mainWindow = Application.Current?.MainWindow as MainWindow;
        var playlists = mainWindow?.Playlists;
        if (addToPlaylistItem != null && playlists != null)
            TrackContextMenuHelper.RebuildAddToPlaylistChildren(addToPlaylistItem, playlists, QueueContextMenu_PlaylistSubmenuClick);

        var showInArtistsItem = TrackContextMenuHelper.FindMenuItemByHeader(menu.Items, "Show in Artists");
        var showInSongsItem = TrackContextMenuHelper.FindMenuItemByHeader(menu.Items, "Show in Songs");
        var showInAlbumsItem = TrackContextMenuHelper.FindMenuItemByHeader(menu.Items, "Show in Albums");
        var showInQueueItem = TrackContextMenuHelper.FindMenuItemByHeader(menu.Items, "Show in Queue");
        bool isInQueue = mainWindow?.IsTrackInQueue(_contextMenuSong) == true;
        // Popout is not the main Queue page; do not pass "Queue" or the helper hides Show in Queue as redundant.
        TrackContextMenuHelper.ApplyShowInMenuVisibility(
            contextMenuViewName: null,
            showInArtistsItem,
            showInSongsItem,
            showInAlbumsItem,
            showInQueueItem,
            isInQueue);
    }

    private void QueueContextMenu_PlaylistSubmenuClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuSong == null || sender is not MenuItem mi || mi.Tag is not Playlist playlist)
            return;
        AddTrackToPlaylistRequested?.Invoke(this, (_contextMenuSong, playlist));
    }

    private void QueueContextMenu_PlayNextClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) PlayNextRequested?.Invoke(this, _contextMenuSong); }
    private void QueueContextMenu_AddToQueueClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) AddToQueueRequested?.Invoke(this, _contextMenuSong); }
    private void QueueContextMenu_AddToPlaylistClick(object sender, RoutedEventArgs e) { }
    private void QueueContextMenu_NewPlaylistClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) CreateNewPlaylistWithTrackRequested?.Invoke(this, _contextMenuSong); }
    private void QueueContextMenu_InfoClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) InfoRequested?.Invoke(this, _contextMenuSong); }
    private void QueueContextMenu_ShowInArtistsClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) ShowInArtistsRequested?.Invoke(this, _contextMenuSong); }
    private void QueueContextMenu_ShowInSongsClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) ShowInSongsRequested?.Invoke(this, _contextMenuSong); }
    private void QueueContextMenu_ShowInAlbumsClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) ShowInAlbumsRequested?.Invoke(this, _contextMenuSong); }
    private void QueueContextMenu_ShowInQueueClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) ShowInQueueRequested?.Invoke(this, _contextMenuSong); }
    private void QueueContextMenu_ShowInExplorerClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) ShowInExplorerRequested?.Invoke(this, _contextMenuSong); }
    private void QueueContextMenu_RemoveFromLibraryClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) RemoveFromLibraryRequested?.Invoke(this, _contextMenuSong); }
    private void QueueContextMenu_DeleteClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) DeleteRequested?.Invoke(this, _contextMenuSong); }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var currentHeight = !double.IsNaN(PopupBorder.Height) ? PopupBorder.Height : PopupBorder.ActualHeight;
        if (currentHeight <= 0)
            currentHeight = UILayoutConstants.CompactPopupMinHeight;

        var maxHeight = CompactPopupHeightHelper.GetAvailableMaxHeight(this);
        var newHeight = Math.Clamp(currentHeight + e.VerticalChange, UILayoutConstants.CompactPopupMinHeight, maxHeight);
        PopupBorder.Height = newHeight;
    }
}
