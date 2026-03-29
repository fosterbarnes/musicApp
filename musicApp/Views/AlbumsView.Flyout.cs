using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using musicApp;
using musicApp.Constants;
using musicApp.Helpers;

namespace musicApp.Views
{
    public partial class AlbumsView
    {
        public static string FlyoutSelectionKeyForSong(Song s)
        {
            if (!string.IsNullOrWhiteSpace(s.FilePath))
                return "p:" + s.FilePath;
            return "m:" + (s.Title ?? "") + "|" + (s.Artist ?? "") + "|" + (s.Album ?? "");
        }

        public bool IsFlyoutTrackSelected(Song? song)
        {
            if (song == null)
                return false;
            return _flyoutSelectedKeys.Contains(FlyoutSelectionKeyForSong(song));
        }

        private void BumpFlyoutSelectionRevision() =>
            SetValue(FlyoutSelectionRevisionProperty, (int)GetValue(FlyoutSelectionRevisionProperty) + 1);

        private void ClearFlyoutTrackSelection()
        {
            _flyoutSelectedKeys.Clear();
            _flyoutAnchorIndex = -1;
            SelectedFlyoutTrackFilePath = null;
            BumpFlyoutSelectionRevision();
        }

        private void ApplyProgrammaticFlyoutSelection(string? filePath)
        {
            _flyoutSelectedKeys.Clear();
            _flyoutAnchorIndex = -1;
            SelectedFlyoutTrackFilePath = filePath;
            if (!string.IsNullOrWhiteSpace(filePath) && _currentFlyout?.Tracks != null)
            {
                var list = _currentFlyout.Tracks;
                for (int i = 0; i < list.Count; i++)
                {
                    var t = list[i];
                    if (!string.IsNullOrWhiteSpace(t.FilePath) &&
                        string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        _flyoutSelectedKeys.Add(FlyoutSelectionKeyForSong(t));
                        _flyoutAnchorIndex = i;
                        break;
                    }
                }
            }
            BumpFlyoutSelectionRevision();
        }

        private void SetFlyoutPrimaryTrack(Song track) =>
            SelectedFlyoutTrackFilePath = string.IsNullOrWhiteSpace(track.FilePath) ? null : track.FilePath;

        private int FindFlyoutTrackIndex(Song track)
        {
            if (_currentFlyout?.Tracks == null)
                return -1;
            var list = _currentFlyout.Tracks;
            var key = FlyoutSelectionKeyForSong(track);
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(FlyoutSelectionKeyForSong(list[i]), key, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private void FilterFlyoutSelectionToCurrentTracks()
        {
            if (_currentFlyout?.Tracks == null || _flyoutSelectedKeys.Count == 0)
                return;
            var valid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in _currentFlyout.Tracks)
                valid.Add(FlyoutSelectionKeyForSong(t));
            _flyoutSelectedKeys.RemoveWhere(k => !valid.Contains(k));
            BumpFlyoutSelectionRevision();
        }

        private void EnqueueFlyoutFullSizeArtLoad(AlbumFlyoutItem flyout, Song repForFull, AlbumGridItem? requireSelectedAlbum)
        {
            var flyoutRef = flyout;
            _ = Task.Run(() =>
            {
                var full = AlbumArtThumbnailHelper.LoadFullSizeForTrack(repForFull);
                if (full == null)
                    return;
                Dispatcher.InvokeAsync(() =>
                {
                    if (!ReferenceEquals(_currentFlyout, flyoutRef))
                        return;
                    if (requireSelectedAlbum != null && !ReferenceEquals(_selectedAlbum, requireSelectedAlbum))
                        return;
                    flyoutRef.AlbumArtSource = full;
                });
            });
        }

        private void PopulateFlyoutTrackLists(AlbumFlyoutItem flyout, string albumTitle, string albumArtistKey)
        {
            if (_itemsSource is not IEnumerable<Song> songs)
                return;

            flyout.Tracks = AlbumTrackOrder.SortByAlbumSequence(
                songs.Where(s =>
                    string.Equals(s.Album, albumTitle, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        !string.IsNullOrWhiteSpace(s.AlbumArtist) ? s.AlbumArtist : s.Artist,
                        albumArtistKey,
                        StringComparison.OrdinalIgnoreCase)));

            int half = (int)Math.Ceiling(flyout.Tracks.Count / 2.0);
            flyout.TracksColumn1 = flyout.Tracks.Take(half).ToList();
            flyout.TracksColumn2 = flyout.Tracks.Skip(half).ToList();
            flyout.AlbumMetadata = AlbumMetadataText.BuildAlbumSummary(flyout.Tracks);
            FlyoutPanelHeight = CalculateFlyoutHeight(flyout.Tracks.Count);
            FilterFlyoutSelectionToCurrentTracks();
        }

        private void PatchOpenFlyoutForGroup(string albumTitle, string albumArtistKey)
        {
            if (_currentFlyout == null)
                return;

            var flyout = _currentFlyout;
            if (_selectedAlbum?.RepresentativeTrack is Song repMeta)
            {
                flyout.Genre = repMeta.Genre ?? "";
                flyout.Year = repMeta.Year > 0 ? repMeta.Year.ToString() : "";
            }

            flyout.AlbumArtSource = _selectedAlbum?.AlbumArtSource;

            PopulateFlyoutTrackLists(flyout, albumTitle, albumArtistKey);
            FlyoutPanelWidth = GetWrapPanelWidth();
            if (_selectedAlbum != null)
                UpdateFlyoutArrowOffset(_selectedAlbum);

            if (_selectedAlbum?.RepresentativeTrack is Song repForFull)
                EnqueueFlyoutFullSizeArtLoad(flyout, repForFull, requireSelectedAlbum: null);
        }

        private void ShowAlbumDetail(AlbumGridItem album, bool bringFlyoutIntoView = true)
        {
            ClearFlyoutTrackSelection();

            if (_currentFlyout != null)
            {
                _albumItems.Remove(_currentFlyout);
                _currentFlyout = null;
                AlbumGrid.UpdateLayout();
            }

            _selectedAlbum = album;

            var flyout = new AlbumFlyoutItem
            {
                AlbumTitle = album.AlbumTitle,
                Artist = album.Artist,
                Genre = album.RepresentativeTrack?.Genre ?? "",
                Year = album.RepresentativeTrack?.Year > 0 ? album.RepresentativeTrack.Year.ToString() : "",
                AlbumArtSource = album.AlbumArtSource
            };

            if (album.AlbumArtSource == null && album.RepresentativeTrack is Song repForThumb)
            {
                _ = Task.Run(() =>
                {
                    var img = AlbumArtThumbnailHelper.LoadForTrack(repForThumb);
                    if (img != null)
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            album.AlbumArtSource = img;
                            if (_selectedAlbum == album)
                                flyout.AlbumArtSource = img;
                        });
                    }
                });
            }

            if (album.RepresentativeTrack is Song repForFull)
                EnqueueFlyoutFullSizeArtLoad(flyout, repForFull, requireSelectedAlbum: album);

            PopulateFlyoutTrackLists(flyout, album.AlbumTitle, album.Artist);

            int albumIndex = _albumItems.IndexOf(album);
            int insertIndex = GetRowEndIndex(albumIndex);

            FlyoutPanelWidth = GetWrapPanelWidth();
            UpdateFlyoutArrowOffset(album);

            _currentFlyout = flyout;
            _albumItems.Insert(insertIndex, flyout);

            if (!bringFlyoutIntoView)
                return;

            Dispatcher.InvokeAsync(() =>
            {
                var container = AlbumGrid.ItemContainerGenerator.ContainerFromItem(flyout) as FrameworkElement;
                container?.BringIntoView();
            }, DispatcherPriority.Background);
        }

        private void CloseAlbumDetail()
        {
            _selectedAlbum = null;
            FlyoutPanelHeight = FlyoutHeight;
            if (_currentFlyout != null)
            {
                _albumItems.Remove(_currentFlyout);
                _currentFlyout = null;
            }

            ClearFlyoutTrackSelection();
        }

        private void RefreshOpenFlyoutLayout()
        {
            if (_isRefreshingFlyoutLayout || _currentFlyout == null || _selectedAlbum == null)
                return;

            if (!_albumItems.Contains(_selectedAlbum))
            {
                CloseAlbumDetail();
                return;
            }

            _isRefreshingFlyoutLayout = true;
            try
            {
                double preservedOffset = AlbumScrollViewer?.VerticalOffset ?? 0;

                int existingFlyoutIndex = _albumItems.IndexOf(_currentFlyout);
                if (existingFlyoutIndex >= 0)
                    _albumItems.RemoveAt(existingFlyoutIndex);

                AlbumGrid.UpdateLayout();

                int albumIndex = _albumItems.IndexOf(_selectedAlbum);
                if (albumIndex < 0)
                {
                    _currentFlyout = null;
                    _selectedAlbum = null;
                    return;
                }

                int insertIndex = GetRowEndIndex(albumIndex);
                _albumItems.Insert(insertIndex, _currentFlyout);

                FlyoutPanelWidth = GetWrapPanelWidth();
                FlyoutPanelHeight = CalculateFlyoutHeight(_currentFlyout.Tracks.Count);
                UpdateFlyoutArrowOffset(_selectedAlbum);

                if (AlbumScrollViewer != null)
                    AlbumScrollViewer.ScrollToVerticalOffset(Math.Min(preservedOffset, AlbumScrollViewer.ScrollableHeight));
            }
            finally
            {
                _isRefreshingFlyoutLayout = false;
            }
        }

        private static double CalculateFlyoutHeight(int trackCount)
        {
            int rowsPerColumn = (int)Math.Ceiling(Math.Max(0, trackCount) / 2.0);
            if (rowsPerColumn <= BaseFlyoutRowsPerColumn)
                return FlyoutHeight;

            int overflowRows = rowsPerColumn - BaseFlyoutRowsPerColumn;
            return FlyoutHeight + (overflowRows * FlyoutTrackRowHeight);
        }

        private void FlyoutTrack_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not Song track || _currentFlyout == null)
                return;

            if (e.ClickCount == 2)
            {
                SetFlyoutPrimaryTrack(track);
                PlayTrackRequested?.Invoke(this, track);
                e.Handled = true;
                return;
            }

            var tracks = _currentFlyout.Tracks;
            int idx = FindFlyoutTrackIndex(track);
            if (idx < 0)
                return;

            var mods = Keyboard.Modifiers;
            if ((mods & ModifierKeys.Shift) != 0)
            {
                int anchor = _flyoutAnchorIndex >= 0 ? _flyoutAnchorIndex : idx;
                int lo = Math.Min(anchor, idx);
                int hi = Math.Max(anchor, idx);
                _flyoutSelectedKeys.Clear();
                for (int i = lo; i <= hi && i < tracks.Count; i++)
                    _flyoutSelectedKeys.Add(FlyoutSelectionKeyForSong(tracks[i]));
                SetFlyoutPrimaryTrack(track);
                BumpFlyoutSelectionRevision();
                return;
            }

            if ((mods & ModifierKeys.Control) != 0)
            {
                var key = FlyoutSelectionKeyForSong(track);
                if (!_flyoutSelectedKeys.Remove(key))
                    _flyoutSelectedKeys.Add(key);
                _flyoutAnchorIndex = idx;
                SetFlyoutPrimaryTrack(track);
                BumpFlyoutSelectionRevision();
                return;
            }

            _flyoutSelectedKeys.Clear();
            _flyoutSelectedKeys.Add(FlyoutSelectionKeyForSong(track));
            _flyoutAnchorIndex = idx;
            SetFlyoutPrimaryTrack(track);
            BumpFlyoutSelectionRevision();
        }

        private void FlyoutTrack_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not Song track)
                return;
            if (_flyoutSelectedKeys.Count > 1 && _flyoutSelectedKeys.Contains(FlyoutSelectionKeyForSong(track)))
                e.Handled = true;
        }

        private void FlyoutArtist_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentFlyout != null && !string.IsNullOrWhiteSpace(_currentFlyout.Artist))
                ArtistNavigationRequested?.Invoke(this, _currentFlyout.Artist);
        }

        private void FlyoutGenre_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentFlyout != null && !string.IsNullOrWhiteSpace(_currentFlyout.Genre))
                GenreNavigationRequested?.Invoke(this, _currentFlyout.Genre);
        }

        private List<Song> GetFlyoutSelectedSongs()
        {
            var list = new List<Song>();
            if (_currentFlyout?.Tracks == null || _flyoutSelectedKeys.Count == 0)
                return list;
            foreach (var t in _currentFlyout.Tracks)
            {
                if (t != null && _flyoutSelectedKeys.Contains(FlyoutSelectionKeyForSong(t)))
                    list.Add(t);
            }
            return list;
        }

        private void AlbumTrackContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            _contextMenuSong = null;
            if (sender is not ContextMenu menu || menu.PlacementTarget is not DependencyObject placement)
                return;

            if (_flyoutSelectedKeys.Count > 1)
                return;

            if (TrackContextMenuHelper.TryResolveSong(placement, out var track) && track != null)
            {
                _contextMenuSong = track;
                SelectedFlyoutTrackFilePath = string.IsNullOrWhiteSpace(track.FilePath) ? null : track.FilePath;
            }

            var addToPlaylistItem = TrackContextMenuHelper.FindMenuItemByHeader(menu.Items, "Add to Playlist");
            var mainWindow = Application.Current?.MainWindow as MainWindow;
            var playlists = mainWindow?.Playlists;
            if (addToPlaylistItem != null && playlists != null)
                TrackContextMenuHelper.RebuildAddToPlaylistChildren(addToPlaylistItem, playlists, AlbumContextMenu_PlaylistSubmenuClick);

            var showInArtistsItem = TrackContextMenuHelper.FindMenuItemByHeader(menu.Items, "Show in Artists");
            var showInSongsItem = TrackContextMenuHelper.FindMenuItemByHeader(menu.Items, "Show in Songs");
            var showInAlbumsItem = TrackContextMenuHelper.FindMenuItemByHeader(menu.Items, "Show in Albums");
            var showInQueueItem = TrackContextMenuHelper.FindMenuItemByHeader(menu.Items, "Show in Queue");
            bool isInQueue = _contextMenuSong != null && mainWindow?.IsTrackInQueue(_contextMenuSong) == true;
            var viewTag = BrowseMode == AlbumsBrowseMode.RecentlyAdded ? "RecentlyAdded" : "Albums";
            TrackContextMenuHelper.ApplyShowInMenuVisibility(
                viewTag, showInArtistsItem, showInSongsItem, showInAlbumsItem, showInQueueItem, isInQueue);
        }

        private void AlbumContextMenu_PlaylistSubmenuClick(object sender, RoutedEventArgs e)
        {
            if (_contextMenuSong == null || sender is not MenuItem mi || mi.Tag is not Playlist playlist)
                return;
            AddTrackToPlaylistRequested?.Invoke(this, (_contextMenuSong, playlist));
        }

        private void AlbumContextMenu_PlayNextClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) PlayNextRequested?.Invoke(this, _contextMenuSong); }
        private void AlbumContextMenu_AddToQueueClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) AddToQueueRequested?.Invoke(this, _contextMenuSong); }
        private void AlbumContextMenu_NewPlaylistClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) CreateNewPlaylistWithTrackRequested?.Invoke(this, _contextMenuSong); }
        private void AlbumContextMenu_ShowInArtistsClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) ShowInArtistsRequested?.Invoke(this, _contextMenuSong); }
        private void AlbumContextMenu_ShowInAlbumsClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) ShowInAlbumsRequested?.Invoke(this, _contextMenuSong); }
        private void AlbumContextMenu_ShowInSongsClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) ShowInSongsRequested?.Invoke(this, _contextMenuSong); }
        private void AlbumContextMenu_ShowInQueueClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) ShowInQueueRequested?.Invoke(this, _contextMenuSong); }
        private void AlbumContextMenu_InfoClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) InfoRequested?.Invoke(this, _contextMenuSong); }
        private void AlbumContextMenu_ShowInExplorerClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) ShowInExplorerRequested?.Invoke(this, _contextMenuSong); }
        private void AlbumContextMenu_RemoveFromLibraryClick(object sender, RoutedEventArgs e)
        {
            var songs = GetFlyoutSelectedSongs();
            if (songs.Count == 0 && _contextMenuSong != null)
                songs.Add(_contextMenuSong);
            if (songs.Count == 0) return;
            RemoveFromLibraryRequested?.Invoke(this, songs);
        }
        private void AlbumContextMenu_DeleteClick(object sender, RoutedEventArgs e) { if (_contextMenuSong != null) DeleteRequested?.Invoke(this, _contextMenuSong); }

        private int GetRowEndIndex(int albumIndex)
        {
            var gen = AlbumGrid.ItemContainerGenerator;
            var clickedContainer = gen.ContainerFromIndex(albumIndex) as FrameworkElement;
            if (clickedContainer == null) return albumIndex + 1;

            GeneralTransform clickedTransform;
            try { clickedTransform = clickedContainer.TransformToAncestor(AlbumScrollViewer); }
            catch { return albumIndex + 1; }

            double clickedY = clickedTransform.Transform(new Point(0, 0)).Y;

            int lastOnRow = albumIndex;
            for (int i = albumIndex + 1; i < _albumItems.Count; i++)
            {
                if (_albumItems[i] is not AlbumGridItem) break;

                var container = gen.ContainerFromIndex(i) as FrameworkElement;
                if (container == null) break;

                GeneralTransform transform;
                try { transform = container.TransformToAncestor(AlbumScrollViewer); }
                catch { break; }

                if (Math.Abs(transform.Transform(new Point(0, 0)).Y - clickedY) > 5) break;
                lastOnRow = i;
            }

            return lastOnRow + 1;
        }

        private double GetWrapPanelWidth()
        {
            var wrapPanel = FindVisualChild<WrapPanel>(AlbumGrid);
            return wrapPanel?.ActualWidth ?? Math.Max(UILayoutConstants.AlbumWrapFallbackWidth, ActualWidth - UILayoutConstants.AlbumWrapHorizontalPadding);
        }

        private void UpdateFlyoutArrowOffset(AlbumGridItem album)
        {
            var wrapPanel = FindVisualChild<WrapPanel>(AlbumGrid);
            var albumContainer = AlbumGrid.ItemContainerGenerator.ContainerFromItem(album) as FrameworkElement;
            if (wrapPanel == null || albumContainer == null)
            {
                FlyoutArrowOffset = new Thickness(0);
                return;
            }

            try
            {
                var pos = albumContainer.TransformToAncestor(wrapPanel).Transform(new Point(0, 0));
                double arrowX = pos.X + CurrentTileSize / 2 - 10;
                FlyoutArrowOffset = new Thickness(arrowX, 0, 0, 0);
            }
            catch
            {
                FlyoutArrowOffset = new Thickness(0);
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found) return found;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private void AlbumItem_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is AlbumGridItem item)
            {
                AlbumClicked?.Invoke(this, item);

                if (_selectedAlbum == item)
                    CloseAlbumDetail();
                else
                    ShowAlbumDetail(item);
            }
        }
    }
}
