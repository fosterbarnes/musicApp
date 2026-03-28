using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using musicApp;

namespace musicApp.Views
{
    public partial class TrackContextMenu : ResourceDictionary
    {
        // Dynamic playlist submenu and "Show in *" visibility live on TrackListView.ContextMenu_Opening (TrackContextMenuHelper).

        private void AddToPlaylistMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetTrackListView(sender, out var view, out var song))
                view.RequestAddToPlaylist(song);
        }

        private void PlayNextMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetTrackListView(sender, out var view, out var song))
                view.RequestPlayNext(song);
        }

        private void AddToQueueMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetTrackListView(sender, out var view, out var song))
                view.RequestAddToQueue(song);
        }

        private void InfoMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetTrackListView(sender, out var view, out var song))
                view.RequestInfo(song);
        }

        private void ShowInArtistsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetTrackListView(sender, out var view, out var song))
                view.RequestShowInArtists(song);
        }

        private void ShowInSongsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetTrackListView(sender, out var view, out var song))
                view.RequestShowInSongs(song);
        }

        private void ShowInQueueMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetTrackListView(sender, out var view, out var song))
                view.RequestShowInQueue(song);
        }

        private void ShowInAlbumsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetTrackListView(sender, out var view, out var song))
                view.RequestShowInAlbums(song);
        }

        private void ShowInExplorerMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetTrackListView(sender, out var view, out var song))
                view.RequestShowInExplorer(song);
        }

        private void RemoveFromLibraryMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetTrackListView(sender, out var view, out var song))
                view.RequestRemoveFromLibrary(song);
        }

        private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetTrackListView(sender, out var view, out var song))
                view.RequestDelete(song);
        }

        private void RemoveFromPlaylistMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetTrackListView(sender, out var view, out var song))
                view.RequestRemoveFromPlaylist(song);
        }

        private void NewPlaylistMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetTrackListViewFromSubmenu(sender, out var view, out var song))
                view.RequestCreateNewPlaylistWithTrack(song);
        }

        /// <summary>Gets the TrackListView and selected Song when the click is from a submenu item (e.g. "New Playlist" under "Add to Playlist").</summary>
        private static bool TryGetTrackListViewFromSubmenu(object eventSender, out TrackListView trackListView, out Song song)
        {
            trackListView = null!;
            song = null!;
            if (eventSender is not MenuItem menuItem)
                return false;
            var parentItem = menuItem.Parent as MenuItem;
            var contextMenu = parentItem?.Parent as ContextMenu;
            var listView = contextMenu?.PlacementTarget as ListView;
            if (listView?.SelectedItem is not Song s)
                return false;
            var parent = VisualTreeHelper.GetParent(listView);
            while (parent != null)
            {
                if (parent is TrackListView tl)
                {
                    trackListView = tl;
                    song = s;
                    return true;
                }
                parent = VisualTreeHelper.GetParent(parent);
            }
            return false;
        }

        private static bool TryGetTrackListView(object eventSender, out TrackListView trackListView, out Song song)
        {
            trackListView = null!;
            song = null!;
            if (eventSender is not MenuItem menuItem)
                return false;
            var contextMenu = menuItem.Parent as ContextMenu;
            var listView = contextMenu?.PlacementTarget as ListView;
            if (listView?.SelectedItem is not Song s)
                return false;
            var parent = VisualTreeHelper.GetParent(listView);
            while (parent != null)
            {
                if (parent is TrackListView tl)
                {
                    trackListView = tl;
                    song = s;
                    return true;
                }
                parent = VisualTreeHelper.GetParent(parent);
            }
            return false;
        }
    }
}
