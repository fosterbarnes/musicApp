using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using musicApp;
using musicApp.Constants;
using musicApp.Helpers;

namespace musicApp.Views
{
    public partial class TrackListView : UserControl
    {
        public static readonly DependencyProperty ViewNameProperty = DependencyProperty.Register(
            nameof(ViewName), typeof(string), typeof(TrackListView),
            new PropertyMetadata("Songs", OnViewNameChanged));

        public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
            nameof(ItemsSource), typeof(System.Collections.IEnumerable), typeof(TrackListView),
            new PropertyMetadata(null, OnItemsSourceChanged));

        public static readonly DependencyProperty SingleClickPlaysProperty = DependencyProperty.Register(
            nameof(SingleClickPlays), typeof(bool), typeof(TrackListView), new PropertyMetadata(false));

        public static readonly DependencyProperty ContextMenuViewNameProperty = DependencyProperty.Register(
            nameof(ContextMenuViewName), typeof(string), typeof(TrackListView), new PropertyMetadata(string.Empty));

        public string ViewName
        {
            get => (string)GetValue(ViewNameProperty);
            set => SetValue(ViewNameProperty, value);
        }

        public System.Collections.IEnumerable? ItemsSource
        {
            get => (System.Collections.IEnumerable?)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        /// <summary>When true, single-click plays the track (e.g. Queue, Recently Played, Artists, Genres). When false, only double-click plays (Songs view).</summary>
        public bool SingleClickPlays
        {
            get => (bool)GetValue(SingleClickPlaysProperty);
            set => SetValue(SingleClickPlaysProperty, value);
        }

        /// <summary>Optional context-menu view identity override for host views that reuse a common TrackList ViewName.</summary>
        public string ContextMenuViewName
        {
            get => (string)GetValue(ContextMenuViewNameProperty);
            set => SetValue(ContextMenuViewNameProperty, value);
        }

        public event EventHandler<Song>? PlayTrackRequested;

        public event EventHandler<Song>? AddToPlaylistRequested;
        public event EventHandler<(Song track, Playlist playlist)>? AddTrackToPlaylistRequested;
        public event EventHandler<(Song track, Playlist playlist)>? RemoveFromPlaylistRequested;
        public event EventHandler<Song>? CreateNewPlaylistWithTrackRequested;
        public event EventHandler<Song>? PlayNextRequested;
        public event EventHandler<Song>? AddToQueueRequested;
        public event EventHandler<Song>? InfoRequested;
        public event EventHandler<Song>? ShowInArtistsRequested;
        public event EventHandler<Song>? ShowInSongsRequested;
        public event EventHandler<Song>? ShowInAlbumsRequested;
        public event EventHandler<Song>? ShowInQueueRequested;
        public event EventHandler<Song>? ShowInExplorerRequested;
        public event EventHandler<Song>? RemoveFromLibraryRequested;
        public event EventHandler<Song>? DeleteRequested;

        public Song? SelectedTrack => lstTracks.SelectedItem as Song;

        public void RefreshItemBindings()
        {
            try
            {
                lstTracks.Items.Refresh();
            }
            catch
            {
                // ignore
            }
        }

        public void ScrollToSong(Song song)
        {
            lstTracks.SelectedItem = song;
            lstTracks.ScrollIntoView(song);
        }

        /// <summary>When set (e.g. by PlaylistsView), the track context menu shows "Remove from Playlist" and it removes the track from this playlist.</summary>
        public Playlist? CurrentPlaylist { get; set; }

        private readonly HashSet<GridViewColumnHeader> _wiredHeaders = new();
        private readonly HashSet<GridViewColumn> _wiredColumnWidthHandlers = new();
        private (string column, ListSortDirection direction) _sortState;
        private ContextMenu? _columnContextMenu;
        private SettingsManager.AppSettings? _cachedSettings;
        private DispatcherTimer? _columnWidthSaveTimer;
        private bool _columnWidthDirty;
        private bool _contextMenuOpeningHandlerAdded;

        public TrackListView()
        {
            InitializeComponent();
            TrackListColumnConfig.Initialize();
            Loaded += TrackListView_Loaded;
            lstTracks.SelectionChanged += LstTracks_SelectionChanged;
            lstTracks.MouseDoubleClick += LstTracks_MouseDoubleClick;
            lstTracks.SizeChanged += LstTracks_SizeChanged;
            SetupColumnWidthTracking();
        }

        private void LstTracks_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateLastColumnFillWidth();
        }

        private static void OnViewNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TrackListView view && view.lstTracks != null)
                view.BuildGridViewColumns();
        }

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TrackListView view)
                view.lstTracks.ItemsSource = view.ItemsSource;
        }

        private void TrackListView_Loaded(object sender, RoutedEventArgs e)
        {
            lstTracks.ItemsSource = ItemsSource;
            EnsureSettingsLoaded();
            BuildGridViewColumns();
            WireUpAddToPlaylistSubmenu();
        }

        private void WireUpAddToPlaylistSubmenu()
        {
            if (_contextMenuOpeningHandlerAdded) return;
            _contextMenuOpeningHandlerAdded = true;
            lstTracks.AddHandler(FrameworkElement.ContextMenuOpeningEvent,
                (ContextMenuEventHandler)ContextMenu_Opening);
        }

        private void ContextMenu_Opening(object sender, ContextMenuEventArgs e)
        {
            var listView = sender as ListView;
            var contextMenu = listView?.ContextMenu;
            if (contextMenu?.Items == null) return;
            var selectedSong = listView?.SelectedItem as Song;
            var addToPlaylistItem = FindMenuItemByHeader(contextMenu.Items, "Add to Playlist");
            var mainWindow = Application.Current?.MainWindow as MainWindow;
            var playlists = mainWindow?.Playlists;
            if (addToPlaylistItem != null && playlists != null)
            {
                // Keep "New Playlist" (0) and Separator (1); remove rest and add current playlists
                while (addToPlaylistItem.Items.Count > 2)
                    addToPlaylistItem.Items.RemoveAt(2);
                foreach (var playlist in playlists)
                {
                    var mi = new MenuItem
                    {
                        Header = playlist.Name,
                        Tag = playlist
                    };
                    mi.Click += PlaylistSubmenuItem_Click;
                    addToPlaylistItem.Items.Add(mi);
                }
            }
            // Show "Remove from Playlist" only when we're showing a playlist's tracks (Playlists view).
            var removeFromPlaylistItem = FindMenuItemByHeader(contextMenu.Items, "Remove from Playlist");
            if (removeFromPlaylistItem != null)
                removeFromPlaylistItem.Visibility = CurrentPlaylist != null ? Visibility.Visible : Visibility.Collapsed;

            var showInArtistsItem = FindMenuItemByHeader(contextMenu.Items, "Show in Artists");
            var showInSongsItem = FindMenuItemByHeader(contextMenu.Items, "Show in Songs");
            var showInAlbumsItem = FindMenuItemByHeader(contextMenu.Items, "Show in Albums");
            var showInQueueItem = FindMenuItemByHeader(contextMenu.Items, "Show in Queue");
            bool isInQueue = selectedSong != null && mainWindow?.IsTrackInQueue(selectedSong) == true;
            ApplyShowInMenuVisibility(showInArtistsItem, showInSongsItem, showInAlbumsItem, showInQueueItem, isInQueue);
        }

        private void ApplyShowInMenuVisibility(MenuItem? showInArtistsItem, MenuItem? showInSongsItem, MenuItem? showInAlbumsItem, MenuItem? showInQueueItem, bool isInQueue)
        {
            var contextName = string.IsNullOrWhiteSpace(ContextMenuViewName) ? ViewName : ContextMenuViewName;
            bool showArtists = true;
            bool showSongs = true;
            bool showAlbums = true;
            bool showQueue = isInQueue;

            if (string.Equals(contextName, "Artists", StringComparison.OrdinalIgnoreCase))
            {
                showArtists = false;
            }
            else if (string.Equals(contextName, "Albums", StringComparison.OrdinalIgnoreCase))
            {
                showAlbums = false;
            }
            else if (string.Equals(contextName, "Songs", StringComparison.OrdinalIgnoreCase))
            {
                showSongs = false;
            }
            else if (string.Equals(contextName, "Recently Played", StringComparison.OrdinalIgnoreCase))
            {
                showAlbums = false;
            }
            else if (string.Equals(contextName, "Queue", StringComparison.OrdinalIgnoreCase))
            {
                showQueue = false;
            }

            if (showInArtistsItem != null)
                showInArtistsItem.Visibility = showArtists ? Visibility.Visible : Visibility.Collapsed;
            if (showInSongsItem != null)
                showInSongsItem.Visibility = showSongs ? Visibility.Visible : Visibility.Collapsed;
            if (showInAlbumsItem != null)
                showInAlbumsItem.Visibility = showAlbums ? Visibility.Visible : Visibility.Collapsed;
            if (showInQueueItem != null)
                showInQueueItem.Visibility = showQueue ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PlaylistSubmenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.Tag is not Playlist playlist)
                return;
            var addToPlaylistParent = menuItem.Parent as MenuItem;
            var contextMenu = addToPlaylistParent?.Parent as ContextMenu;
            var listView = contextMenu?.PlacementTarget as ListView;
            if (listView?.SelectedItem is not Song song)
                return;
            AddTrackToPlaylistRequested?.Invoke(this, (song, playlist));
        }

        private static MenuItem? FindMenuItemByHeader(ItemCollection items, string header)
        {
            foreach (var item in items)
            {
                if (item is MenuItem mi && mi.Header?.ToString() == header)
                    return mi;
            }
            return null;
        }

        public void RebuildColumns()
        {
            _cachedSettings = null;
            EnsureSettingsLoaded();
            BuildGridViewColumns();
        }

        private void EnsureSettingsLoaded()
        {
            if (_cachedSettings != null) return;
            _cachedSettings = SettingsManager.Instance.LoadSettingsSync();
        }

        private List<string> GetVisibleColumns()
        {
            EnsureSettingsLoaded();
            if (_cachedSettings?.WindowState?.ColumnVisibility != null &&
                _cachedSettings.WindowState.ColumnVisibility.TryGetValue(ViewName, out var saved) &&
                saved != null && saved.Count > 0)
                return saved;
            if (TrackListColumnConfig.DefaultVisibleColumns.TryGetValue(ViewName, out var defaults))
                return defaults;
            return new List<string> { "Title", "Artist", "Album", "Time" };
        }

        private void TeardownGridViewColumnHandlers(GridView gridView)
        {
            foreach (var header in _wiredHeaders.ToList())
            {
                try { header.Click -= ColumnHeader_Click; }
                catch { /* ignore */ }
            }
            _wiredHeaders.Clear();

            var widthDescriptor = DependencyPropertyDescriptor.FromProperty(GridViewColumn.WidthProperty, typeof(GridViewColumn));
            foreach (var column in gridView.Columns)
            {
                if (!_wiredColumnWidthHandlers.Contains(column)) continue;
                widthDescriptor?.RemoveValueChanged(column, OnGridViewColumnWidthChanged);
            }
            _wiredColumnWidthHandlers.Clear();
        }

        private void OnGridViewColumnWidthChanged(object? sender, EventArgs e)
        {
            if (sender is not GridViewColumn col) return;
            const double minWidth = UILayoutConstants.TrackListMinimumColumnWidth;
            if (!double.IsNaN(col.Width) && col.Width < minWidth)
                col.Width = minWidth;
            MarkColumnWidthDirty();
        }

        private void BuildGridViewColumns()
        {
            try
            {
                var gridView = lstTracks.View as GridView ?? new GridView();
                lstTracks.View = gridView;
                TeardownGridViewColumnHandlers(gridView);
                gridView.Columns.Clear();

                var visibleColumns = GetVisibleColumns();
                if (visibleColumns == null || visibleColumns.Count == 0)
                    visibleColumns = TrackListColumnConfig.DefaultVisibleColumns.TryGetValue(ViewName, out var d) ? d : new List<string> { "Title", "Artist", "Album", "Time" };

                Dictionary<string, double> savedWidths;
                if (_cachedSettings?.WindowState?.ColumnWidths != null &&
                    _cachedSettings.WindowState.ColumnWidths.TryGetValue(ViewName, out var viewWidths))
                    savedWidths = viewWidths;
                else
                    savedWidths = _cachedSettings?.WindowState?.SongsViewColumnWidths ?? new Dictionary<string, double>();

                for (int i = 0; i < visibleColumns.Count; i++)
                {
                    var columnName = visibleColumns[i];
                    if (!TrackListColumnConfig.ColumnDefinitions.TryGetValue(columnName, out var columnDef))
                        continue;
                    bool isLastColumn = (i == visibleColumns.Count - 1);
                    double width = savedWidths.TryGetValue(columnDef.DisplayName, out var w) && w > 0 ? w : columnDef.DefaultWidth;
                    var column = new GridViewColumn
                    {
                        Header = columnDef.DisplayName,
                        Width = width
                    };
                    var binding = new Binding(columnDef.PropertyName);
                    if (columnDef.Converter != null)
                        binding.Converter = columnDef.Converter;
                    var dataTemplate = new DataTemplate();
                    var textBlockFactory = new FrameworkElementFactory(typeof(TextBlock));
                    textBlockFactory.SetBinding(TextBlock.TextProperty, binding);
                    textBlockFactory.SetValue(TextBlock.MarginProperty, new Thickness(-9, 0, 0, 0));
                    textBlockFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
                    dataTemplate.VisualTree = textBlockFactory;
                    column.CellTemplate = dataTemplate;
                    gridView.Columns.Add(column);
                }

                var headerStyle = TryFindResource("CompactGridViewHeaderStyle") as Style;
                if (headerStyle != null)
                    gridView.ColumnHeaderContainerStyle = headerStyle;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateLastColumnFillWidth();
                    LockLastColumnResizeGripper();
                    WireUpHeadersForListView();
                    WireUpColumnWidthMonitoring();
                    WireUpContextMenuForHeaders();
                }), DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TrackListView BuildGridViewColumns {ViewName}: {ex.Message}");
            }
        }

        private void LockLastColumnResizeGripper()
        {
            if (lstTracks.View is not GridView gridView || gridView.Columns.Count == 0) return;
            var lastColumn = gridView.Columns[gridView.Columns.Count - 1];
            foreach (var header in FindVisualChildren<GridViewColumnHeader>(lstTracks))
            {
                if (header.Column != lastColumn) continue;
                foreach (var thumb in FindVisualChildren<Thumb>(header))
                {
                    thumb.IsHitTestVisible = false;
                    return;
                }
                return;
            }
        }

        private void UpdateLastColumnFillWidth()
        {
            if (lstTracks.View is not GridView gridView || gridView.Columns.Count == 0) return;
            var lastColumn = gridView.Columns[gridView.Columns.Count - 1];
            double otherWidth = 0;
            for (int i = 0; i < gridView.Columns.Count - 1; i++)
            {
                var w = gridView.Columns[i].Width;
                if (!double.IsNaN(w) && w > 0) otherWidth += w;
            }
            double available = lstTracks.ActualWidth - SystemParameters.VerticalScrollBarWidth;
            double fillWidth = Math.Max(UILayoutConstants.TrackListMinimumColumnWidth, available - otherWidth);
            lastColumn.Width = fillWidth;
        }

        private void WireUpHeadersForListView()
        {
            foreach (var header in FindVisualChildren<GridViewColumnHeader>(lstTracks))
            {
                if (_wiredHeaders.Contains(header)) continue;
                header.Click += ColumnHeader_Click;
                _wiredHeaders.Add(header);
            }
        }

        private void ColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not GridViewColumnHeader clickedHeader) return;
            var columnName = clickedHeader.Content?.ToString() ?? "";
            var direction = _sortState.column == columnName && _sortState.direction == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
            _sortState = (columnName, direction);
            SortListView(columnName, direction);
        }

        private void SortListView(string columnName, ListSortDirection direction)
        {
            if (lstTracks.ItemsSource == null) return;
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(lstTracks.ItemsSource);
            if (view == null) return;
            view.SortDescriptions.Clear();
            var propertyName = TrackListColumnConfig.ColumnDefinitions.TryGetValue(columnName, out var def)
                ? def.SortPropertyName
                : columnName;
            view.SortDescriptions.Add(new SortDescription(propertyName, direction));
            view.Refresh();
        }

        private void WireUpColumnWidthMonitoring()
        {
            if (lstTracks.View is not GridView gridView) return;
            var descriptor = DependencyPropertyDescriptor.FromProperty(GridViewColumn.WidthProperty, typeof(GridViewColumn));
            if (descriptor == null) return;
            foreach (var column in gridView.Columns)
            {
                if (_wiredColumnWidthHandlers.Contains(column)) continue;
                descriptor.AddValueChanged(column, OnGridViewColumnWidthChanged);
                _wiredColumnWidthHandlers.Add(column);
            }
        }

        private void SetupColumnWidthTracking()
        {
            _columnWidthSaveTimer = new DispatcherTimer(
                UILayoutConstants.ColumnWidthSaveDelay,
                DispatcherPriority.Background,
                ColumnWidthSaveTimer_Tick,
                Dispatcher.CurrentDispatcher);
        }

        private void MarkColumnWidthDirty()
        {
            _columnWidthDirty = true;
            _columnWidthSaveTimer?.Start();
        }

        private async void ColumnWidthSaveTimer_Tick(object? sender, EventArgs e)
        {
            if (!_columnWidthDirty) return;
            _columnWidthDirty = false;
            _columnWidthSaveTimer?.Stop();
            try
            {
                var settings = await SettingsManager.Instance.LoadSettingsAsync();
                if (settings?.WindowState == null) return;
                if (settings.WindowState.ColumnWidths == null)
                    settings.WindowState.ColumnWidths = new Dictionary<string, Dictionary<string, double>>();
                if (lstTracks.View is GridView gridView)
                {
                    var columnWidths = new Dictionary<string, double>();
                    int lastIndex = gridView.Columns.Count - 1;
                    for (int i = 0; i < gridView.Columns.Count; i++)
                    {
                        var column = gridView.Columns[i];
                        if (i == lastIndex) continue; // last column is star-sized; don't persist
                        if (column.Header != null && !double.IsNaN(column.Width))
                            columnWidths[column.Header.ToString() ?? ""] = column.Width;
                    }
                    settings.WindowState.ColumnWidths[ViewName] = columnWidths;
                    if (ViewName == "Songs")
                        settings.WindowState.SongsViewColumnWidths = columnWidths;
                }
                await SettingsManager.Instance.SaveSettingsAsync(settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TrackListView save column widths: {ex.Message}");
            }
        }

        private void WireUpContextMenuForHeaders()
        {
            _columnContextMenu = CreateColumnContextMenu();
            foreach (var header in FindVisualChildren<GridViewColumnHeader>(lstTracks))
                header.ContextMenu = _columnContextMenu;
        }

        private ContextMenu CreateColumnContextMenu()
        {
            var contextMenu = new ContextMenu
            {
                Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)),
                Foreground = new SolidColorBrush(Colors.White)
            };
            var autoSizeItem = new MenuItem { Header = "Auto Size All Columns", Foreground = new SolidColorBrush(Colors.White) };
            autoSizeItem.Click += (s, e) => AutoSizeAllColumns();
            contextMenu.Items.Add(autoSizeItem);
            contextMenu.Items.Add(new Separator());

            var visibleColumns = GetVisibleColumns();
            foreach (var kvp in TrackListColumnConfig.ColumnDefinitions.OrderBy(x => x.Key))
            {
                var columnName = kvp.Key;
                var isVisible = visibleColumns.Contains(columnName);
                var menuItem = new MenuItem
                {
                    Header = kvp.Value.DisplayName,
                    IsCheckable = true,
                    IsChecked = isVisible,
                    Foreground = new SolidColorBrush(Colors.White)
                };
                menuItem.Checked += (s, e) =>
                {
                    var current = GetVisibleColumns();
                    if (!current.Contains(columnName))
                        _ = UpdateColumnVisibilityAsync(new List<string>(current) { columnName });
                };
                menuItem.Unchecked += (s, e) =>
                {
                    var current = GetVisibleColumns();
                    if (current.Count > 1)
                    {
                        var updated = new List<string>(current);
                        updated.Remove(columnName);
                        _ = UpdateColumnVisibilityAsync(updated);
                    }
                    else
                        menuItem.IsChecked = true;
                };
                contextMenu.Items.Add(menuItem);
            }
            return contextMenu;
        }

        private void AutoSizeAllColumns()
        {
            if (lstTracks.View is GridView gridView)
            {
                foreach (var column in gridView.Columns)
                    column.Width = double.NaN;
            }
        }

        private async System.Threading.Tasks.Task UpdateColumnVisibilityAsync(List<string> visibleColumns)
        {
            var settings = await SettingsManager.Instance.LoadSettingsAsync();
            if (settings?.WindowState == null) return;
            if (settings.WindowState.ColumnVisibility == null)
                settings.WindowState.ColumnVisibility = new Dictionary<string, List<string>>();
            settings.WindowState.ColumnVisibility[ViewName] = visibleColumns;
            await SettingsManager.Instance.SaveSettingsAsync(settings);
            _cachedSettings = null;
            EnsureSettingsLoaded();
            BuildGridViewColumns();
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                if (child == null) continue;
                if (child is T t)
                    yield return t;
                foreach (var childOfChild in FindVisualChildren<T>(child))
                    yield return childOfChild;
            }
        }

        private void LstTracks_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!SingleClickPlays) return;
            if (lstTracks.SelectedItem is Song track)
                PlayTrackRequested?.Invoke(this, track);
        }

        private void LstTracks_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (lstTracks.SelectedItem is Song track)
                PlayTrackRequested?.Invoke(this, track);
        }

        public void RequestAddToPlaylist(Song song)
        {
            if (song != null)
                AddToPlaylistRequested?.Invoke(this, song);
        }

        public void RequestRemoveFromPlaylist(Song song)
        {
            if (song != null && CurrentPlaylist != null)
                RemoveFromPlaylistRequested?.Invoke(this, (song, CurrentPlaylist));
        }

        public void RequestCreateNewPlaylistWithTrack(Song song)
        {
            if (song != null)
                CreateNewPlaylistWithTrackRequested?.Invoke(this, song);
        }

        public void RequestPlayNext(Song song)
        {
            if (song != null)
                PlayNextRequested?.Invoke(this, song);
        }

        public void RequestAddToQueue(Song song)
        {
            if (song != null)
                AddToQueueRequested?.Invoke(this, song);
        }

        public void RequestInfo(Song song)
        {
            if (song != null)
                InfoRequested?.Invoke(this, song);
        }

        public void RequestShowInArtists(Song song)
        {
            if (song != null)
                ShowInArtistsRequested?.Invoke(this, song);
        }

        public void RequestShowInSongs(Song song)
        {
            if (song != null)
                ShowInSongsRequested?.Invoke(this, song);
        }

        public void RequestShowInAlbums(Song song)
        {
            if (song != null)
                ShowInAlbumsRequested?.Invoke(this, song);
        }

        public void RequestShowInQueue(Song song)
        {
            if (song != null)
                ShowInQueueRequested?.Invoke(this, song);
        }

        public void RequestShowInExplorer(Song song)
        {
            if (song != null)
                ShowInExplorerRequested?.Invoke(this, song);
        }

        public void RequestRemoveFromLibrary(Song song)
        {
            if (song != null)
                RemoveFromLibraryRequested?.Invoke(this, song);
        }

        public void RequestDelete(Song song)
        {
            if (song != null)
                DeleteRequested?.Invoke(this, song);
        }
    }
}
