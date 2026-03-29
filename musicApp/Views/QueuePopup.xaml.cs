using System;
using System.Collections;
using System.Collections.Generic;
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
    private int _queueAnchorIndex = -1;

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

        var previouslySelected = GetSelectedSongsFromItemsSource(view);

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
        ApplySelectionAfterRebuild(view, rows, previouslySelected);
        bool any = rows.Count > 0;
        view.EmptyQueueText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        view.ScheduleAdjustHeight();
        _ = view.LoadSongRowArtAsync(rows);
    }

    private static List<Song> GetSelectedSongsFromItemsSource(QueuePopupView view)
    {
        var list = new List<Song>();
        if (view.QueueItemsList.ItemsSource is not IEnumerable en)
            return list;
        foreach (var item in en)
        {
            if (item is SongRowViewModel vm && vm.IsSelected)
                list.Add(vm.Song);
        }
        return list;
    }

    private static bool QueueSongMatches(Song row, Song sel)
    {
        if (ReferenceEquals(row, sel))
            return true;
        if (!string.IsNullOrWhiteSpace(sel.FilePath) &&
            !string.IsNullOrWhiteSpace(row.FilePath) &&
            string.Equals(row.FilePath, sel.FilePath, StringComparison.OrdinalIgnoreCase))
            return true;
        return string.Equals(row.Title, sel.Title, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(row.Artist, sel.Artist, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(row.Album, sel.Album, StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplySelectionAfterRebuild(QueuePopupView view, ObservableCollection<SongRowViewModel> rows, IReadOnlyList<Song> selectedSongs)
    {
        foreach (var r in rows)
            r.IsSelected = false;
        view._queueAnchorIndex = -1;
        if (selectedSongs == null || selectedSongs.Count == 0)
            return;

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            foreach (var sel in selectedSongs)
            {
                if (!QueueSongMatches(row.Song, sel))
                    continue;
                row.IsSelected = true;
                if (view._queueAnchorIndex < 0)
                    view._queueAnchorIndex = i;
                break;
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
    public event EventHandler<IReadOnlyList<Song>>? RemoveFromLibraryRequested;
    public event EventHandler<Song>? DeleteRequested;

    /// <summary>First selected row index in list order; toolbar uses one index when multiple rows are selected.</summary>
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

    private void ApplyQueuePointerSelection(SongRowViewModel clicked)
    {
        if (QueueItemsList.ItemsSource is not ObservableCollection<SongRowViewModel> rows)
            return;
        int idx = rows.IndexOf(clicked);
        if (idx < 0)
            return;

        var mods = Keyboard.Modifiers;
        if ((mods & ModifierKeys.Shift) != 0)
        {
            int anchor = _queueAnchorIndex >= 0 ? _queueAnchorIndex : idx;
            int lo = Math.Min(anchor, idx);
            int hi = Math.Max(anchor, idx);
            for (int i = 0; i < rows.Count; i++)
                rows[i].IsSelected = i >= lo && i <= hi;
        }
        else if ((mods & ModifierKeys.Control) != 0)
        {
            clicked.IsSelected = !clicked.IsSelected;
            _queueAnchorIndex = idx;
        }
        else
        {
            foreach (var r in rows)
                r.IsSelected = ReferenceEquals(r, clicked);
            _queueAnchorIndex = idx;
        }
    }

    private int CountSelectedQueueRows()
    {
        if (QueueItemsList.ItemsSource is not IEnumerable en)
            return 0;
        int n = 0;
        foreach (var item in en)
        {
            if (item is SongRowViewModel vm && vm.IsSelected)
                n++;
        }
        return n;
    }

    private List<Song> GetSelectedQueueSongs()
    {
        var list = new List<Song>();
        if (QueueItemsList.ItemsSource is not IEnumerable en)
            return list;
        foreach (var item in en)
        {
            if (item is SongRowViewModel vm && vm.IsSelected && vm.Song != null)
                list.Add(vm.Song);
        }
        return list;
    }

    private void QueueSongItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is SongRowViewModel row)
        {
            ApplyQueuePointerSelection(row);
            e.Handled = true;
        }
    }

    private void QueueSongItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement el || el.DataContext is not SongRowViewModel row)
            return;
        if (CountSelectedQueueRows() > 1 && row.IsSelected)
            e.Handled = true;
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

        if (CountSelectedQueueRows() > 1)
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
    private void QueueContextMenu_RemoveFromLibraryClick(object sender, RoutedEventArgs e)
    {
        var songs = GetSelectedQueueSongs();
        if (songs.Count == 0 && _contextMenuSong != null)
            songs.Add(_contextMenuSong);
        if (songs.Count == 0) return;
        RemoveFromLibraryRequested?.Invoke(this, songs);
    }
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
