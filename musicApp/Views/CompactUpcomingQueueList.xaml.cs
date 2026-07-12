using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using musicApp;
using musicApp.Helpers;

namespace musicApp.Views;

public partial class CompactUpcomingQueueList : UserControl
{
    private const string RowReorderDragFormat = "musicApp.CompactUpcomingQueue.RowReorder";

    private Song? _contextMenuSong;
    private int _queueAnchorIndex = -1;
    private CancellationTokenSource? _queueArtCts;
    private Point _rowReorderPressPos;
    private int _rowReorderSourceIndex = -1;

    public CompactUpcomingQueueList()
    {
        InitializeComponent();
        ScrollWheelDampingHelper.Attach(QueueScrollViewer);
        Unloaded += (_, _) =>
        {
            _queueArtCts?.Cancel();
            _queueArtCts?.Dispose();
            _queueArtCts = null;
        };
    }

    public static readonly DependencyProperty EnableMultiSelectProperty =
        DependencyProperty.Register(nameof(EnableMultiSelect), typeof(bool), typeof(CompactUpcomingQueueList),
            new PropertyMetadata(false));

    public bool EnableMultiSelect
    {
        get => (bool)GetValue(EnableMultiSelectProperty);
        set => SetValue(EnableMultiSelectProperty, value);
    }

    public ScrollViewer ScrollViewer => QueueScrollViewer;
    public Panel ContentPanel => QueueContentPanel;

    public event EventHandler? QueueChanged;
    public event EventHandler? QueueRemoveRequested;
    public event EventHandler<(int fromViewIndex, int toViewIndex)>? TracksReordered;
    public event EventHandler<Song>? SongPlayRequested;
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

    public void SetQueue(IEnumerable? tracks)
    {
        _queueArtCts?.Cancel();
        _queueArtCts?.Dispose();
        _queueArtCts = new CancellationTokenSource();
        var artToken = _queueArtCts.Token;

        var previouslySelected = GetSelectedSongs();

        var rows = new ObservableCollection<SongRowViewModel>();
        if (tracks != null)
        {
            bool skipCurrent = true;
            foreach (var item in tracks)
            {
                if (item is not Song song)
                    continue;
                if (skipCurrent)
                {
                    skipCurrent = false;
                    continue;
                }
                rows.Add(new SongRowViewModel(song));
            }
        }

        ApplySelectionAfterRebuild(rows, previouslySelected);
        QueueItemsList.ItemsSource = rows;
        EmptyQueueText.Visibility = rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        _ = LoadSongRowArtAsync(rows, artToken);
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public int GetSelectedViewIndex()
    {
        if (QueueItemsList.ItemsSource is not IEnumerable en)
            return -1;
        int i = 0;
        foreach (var item in en)
        {
            if (item is SongRowViewModel vm && vm.IsSelected)
                return i + 1;
            i++;
        }
        return -1;
    }

    public Song? GetPrimarySelectedSong()
    {
        var songs = GetSelectedSongs();
        return songs.Count > 0 ? songs[0] : null;
    }

    public List<Song> GetSelectedSongs()
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

    private void ApplySelectionAfterRebuild(
        ObservableCollection<SongRowViewModel> rows,
        IReadOnlyList<Song> selectedSongs)
    {
        foreach (var r in rows)
            r.IsSelected = false;
        _queueAnchorIndex = -1;
        if (selectedSongs == null || selectedSongs.Count == 0)
            return;

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            foreach (var sel in selectedSongs)
            {
                if (!SongIdentity.Matches(row.Song, sel))
                    continue;
                row.IsSelected = true;
                if (_queueAnchorIndex < 0)
                    _queueAnchorIndex = i;
                break;
            }
        }
    }

    private async System.Threading.Tasks.Task LoadSongRowArtAsync(
        ObservableCollection<SongRowViewModel> rows,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var row in rows)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var img = await System.Threading.Tasks.Task.Run(
                        () => AlbumArtThumbnailHelper.LoadForTrack(row.Song), cancellationToken)
                    .ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (img != null)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (!cancellationToken.IsCancellationRequested)
                            row.AlbumArtSource = img;
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ApplyPointerSelection(SongRowViewModel clicked)
    {
        if (QueueItemsList.ItemsSource is not ObservableCollection<SongRowViewModel> rows)
            return;
        int idx = rows.IndexOf(clicked);
        if (idx < 0)
            return;

        if (!EnableMultiSelect)
        {
            foreach (var r in rows)
                r.IsSelected = ReferenceEquals(r, clicked);
            _queueAnchorIndex = idx;
            return;
        }

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

    private int CountSelectedRows()
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

    private FrameworkElement? FindRowElement(SongRowViewModel row) =>
        ListRowReorderHelper.FindRowElementForViewModel(QueueItemsList, row);

    private void QueueSongItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not SongRowViewModel row)
            return;

        ApplyPointerSelection(row);

        if (e.ClickCount == 2 && row.Song != null)
        {
            SongPlayRequested?.Invoke(this, row.Song);
            e.Handled = true;
        }
    }

    private void QueueSongItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not SongRowViewModel row)
            return;
        if (!row.IsSelected)
            ApplyPointerSelection(row);
    }

    private void QueueRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is SongRowViewModel row)
            ApplyPointerSelection(row);
        QueueRemoveRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void QueueMoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement btn || btn.DataContext is not SongRowViewModel row)
            return;
        ApplyPointerSelection(row);
        if (FindRowElement(row) is FrameworkElement rowEl && rowEl.ContextMenu != null)
        {
            rowEl.ContextMenu.PlacementTarget = rowEl;
            rowEl.ContextMenu.IsOpen = true;
        }
        e.Handled = true;
    }

    private void QueueContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        _contextMenuSong = null;
        if (sender is not ContextMenu menu || menu.PlacementTarget is not DependencyObject placement)
            return;

        if (EnableMultiSelect && CountSelectedRows() > 1)
            return;

        if (!TrackContextMenuHelper.TryResolveSong(placement, out var song) || song == null)
            return;

        _contextMenuSong = song;
        var mainWindow = Application.Current?.MainWindow as MainWindow;
        TrackContextMenuHelper.ApplyStandardOpened(
            menu.Items,
            contextMenuViewName: null,
            _contextMenuSong,
            mainWindow?.Playlists,
            QueueContextMenu_PlaylistSubmenuClick);
    }

    private void QueueContextMenu_PlaylistSubmenuClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuSong == null || sender is not MenuItem mi || mi.Tag is not Playlist playlist)
            return;
        AddTrackToPlaylistRequested?.Invoke(this, (_contextMenuSong, playlist));
    }

    private void QueueContextMenu_PlayNextClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuSong != null) PlayNextRequested?.Invoke(this, _contextMenuSong);
    }
    private void QueueContextMenu_AddToQueueClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuSong != null) AddToQueueRequested?.Invoke(this, _contextMenuSong);
    }
    private void QueueContextMenu_AddToPlaylistClick(object sender, RoutedEventArgs e) { }
    private void QueueContextMenu_NewPlaylistClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuSong != null) CreateNewPlaylistWithTrackRequested?.Invoke(this, _contextMenuSong);
    }
    private void QueueContextMenu_InfoClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuSong != null) InfoRequested?.Invoke(this, _contextMenuSong);
    }
    private void QueueContextMenu_ShowInArtistsClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuSong != null) ShowInArtistsRequested?.Invoke(this, _contextMenuSong);
    }
    private void QueueContextMenu_ShowInSongsClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuSong != null) ShowInSongsRequested?.Invoke(this, _contextMenuSong);
    }
    private void QueueContextMenu_ShowInAlbumsClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuSong != null) ShowInAlbumsRequested?.Invoke(this, _contextMenuSong);
    }
    private void QueueContextMenu_ShowInQueueClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuSong != null) ShowInQueueRequested?.Invoke(this, _contextMenuSong);
    }
    private void QueueContextMenu_ShowInExplorerClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuSong != null) ShowInExplorerRequested?.Invoke(this, _contextMenuSong);
    }
    private void QueueContextMenu_RemoveFromLibraryClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuSong != null)
            RemoveFromLibraryRequested?.Invoke(this, new List<Song> { _contextMenuSong });
    }
    private void QueueContextMenu_DeleteClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuSong != null) DeleteRequested?.Invoke(this, _contextMenuSong);
    }

    private void QueueScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ListRowReorderHelper.IsChromeButton(e.OriginalSource as DependencyObject))
        {
            _rowReorderSourceIndex = -1;
            return;
        }

        int idx = ListRowReorderHelper.TryGetRowIndexFromSource(
            e.OriginalSource as DependencyObject,
            QueueItemsList.ItemsSource as ObservableCollection<SongRowViewModel>);
        if (idx < 0)
        {
            _rowReorderSourceIndex = -1;
            return;
        }

        _rowReorderSourceIndex = idx;
        _rowReorderPressPos = e.GetPosition(QueueScrollViewer);
    }

    private void QueueScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_rowReorderSourceIndex < 0 || e.LeftButton != MouseButtonState.Pressed)
            return;

        var pos = e.GetPosition(QueueScrollViewer);
        double dx = pos.X - _rowReorderPressPos.X;
        double dy = pos.Y - _rowReorderPressPos.Y;
        if (Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(dy) < SystemParameters.MinimumVerticalDragDistance)
            return;

        int from = _rowReorderSourceIndex;
        _rowReorderSourceIndex = -1;

        var data = new DataObject();
        data.SetData(RowReorderDragFormat, from);
        try
        {
            DragDrop.DoDragDrop(QueueScrollViewer, data, DragDropEffects.Move);
        }
        catch
        {
        }
        finally
        {
            HideRowReorderInsertLine();
        }
    }

    private void QueueScrollViewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        _rowReorderSourceIndex = -1;

    private void QueueScrollViewer_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(RowReorderDragFormat))
            return;
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        UpdateRowReorderInsertLine(e.GetPosition(QueueScrollViewer));
    }

    private void QueueScrollViewer_DragLeave(object sender, DragEventArgs e) =>
        HideRowReorderInsertLine();

    private void QueueScrollViewer_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(RowReorderDragFormat))
            return;

        if (e.Data.GetData(RowReorderDragFormat) is not int fromIndex || fromIndex < 0)
        {
            e.Handled = true;
            HideRowReorderInsertLine();
            return;
        }

        int toIndex = ComputeTargetViewIndexFromDrop(e.GetPosition(QueueScrollViewer));
        if (toIndex < 0)
        {
            e.Handled = true;
            HideRowReorderInsertLine();
            return;
        }

        int toMove = ListRowReorderHelper.InsertBeforeToMoveIndex(fromIndex, toIndex);
        if (fromIndex == toMove)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            HideRowReorderInsertLine();
            return;
        }

        TracksReordered?.Invoke(this, (fromIndex + 1, toMove + 1));
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        HideRowReorderInsertLine();
    }

    private int ComputeTargetViewIndexFromDrop(Point listPos)
    {
        if (QueueItemsList.ItemsSource is not ObservableCollection<SongRowViewModel> rows)
            return -1;
        ListRowReorderHelper.GetDropGeometry(
            QueueScrollViewer, QueueItemsList, rows, listPos, 0, out int toIndex, out _);
        return toIndex;
    }

    private void UpdateRowReorderInsertLine(Point listPos)
    {
        if (QueueItemsList.ItemsSource is not ObservableCollection<SongRowViewModel> rows)
        {
            HideRowReorderInsertLine();
            return;
        }

        ListRowReorderHelper.GetDropGeometry(
            QueueScrollViewer, QueueItemsList, rows, listPos, 0, out int toIndex, out double lineY);
        if (toIndex < 0)
        {
            HideRowReorderInsertLine();
            return;
        }

        RowReorderInsertLine.Margin = new Thickness(0, lineY, 0, 0);
        RowReorderInsertLine.Visibility = Visibility.Visible;
    }

    private void HideRowReorderInsertLine() =>
        RowReorderInsertLine.Visibility = Visibility.Collapsed;
}
