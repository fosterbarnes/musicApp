using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using musicApp;
using musicApp.Views;

namespace musicApp.Helpers;

internal static class TrackContextMenuHelper
{
    public static MenuItem? FindMenuItemByHeader(ItemCollection items, string header)
    {
        foreach (var item in items)
        {
            if (item is MenuItem mi && mi.Header?.ToString() == header)
                return mi;
        }
        return null;
    }

    public static bool TryResolveSong(DependencyObject? placementTarget, out Song? song)
    {
        song = null;
        if (placementTarget == null)
            return false;

        if (placementTarget is FrameworkElement fe)
        {
            switch (fe.DataContext)
            {
                case Song s:
                    song = s;
                    return true;
                case SongRowViewModel sr:
                    song = sr.Song;
                    return song != null;
                case AlbumRowViewModel ar when ar.Album.Songs.Count > 0:
                    song = ar.Album.Songs[0];
                    return true;
            }
        }

        if (placementTarget is ListView lv && lv.SelectedItem is Song s2)
        {
            song = s2;
            return true;
        }

        return false;
    }

    public static void RebuildAddToPlaylistChildren(MenuItem addToPlaylistRoot, IEnumerable<Playlist> playlists, RoutedEventHandler playlistItemClick)
    {
        while (addToPlaylistRoot.Items.Count > 2)
            addToPlaylistRoot.Items.RemoveAt(2);
        foreach (var playlist in playlists)
        {
            var mi = new MenuItem { Header = playlist.Name, Tag = playlist };
            mi.Click += playlistItemClick;
            addToPlaylistRoot.Items.Add(mi);
        }
    }

    public static void ApplyRemoveFromPlaylistVisibility(MenuItem? item, bool visible)
    {
        if (item != null)
            item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>contextMenuViewName: view hosting the list (e.g. Artists, Albums). Null/empty = no redundant hiding (e.g. Search popup).</summary>
    public static void ApplyShowInMenuVisibility(
        string? contextMenuViewName,
        MenuItem? showInArtistsItem,
        MenuItem? showInSongsItem,
        MenuItem? showInAlbumsItem,
        MenuItem? showInQueueItem,
        bool isInQueue)
    {
        var contextName = string.IsNullOrWhiteSpace(contextMenuViewName) ? "" : contextMenuViewName.Trim();
        bool showArtists = true;
        bool showSongs = true;
        bool showAlbums = true;
        bool showQueue = isInQueue;

        if (string.Equals(contextName, "Artists", StringComparison.OrdinalIgnoreCase))
            showArtists = false;
        else if (string.Equals(contextName, "Albums", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(contextName, "RecentlyAdded", StringComparison.OrdinalIgnoreCase))
            showAlbums = false;
        else if (string.Equals(contextName, "Songs", StringComparison.OrdinalIgnoreCase))
            showSongs = false;
        else if (string.Equals(contextName, "Recently Played", StringComparison.OrdinalIgnoreCase))
            showAlbums = false;
        else if (string.Equals(contextName, "Queue", StringComparison.OrdinalIgnoreCase))
            showQueue = false;

        if (showInArtistsItem != null)
            showInArtistsItem.Visibility = showArtists ? Visibility.Visible : Visibility.Collapsed;
        if (showInSongsItem != null)
            showInSongsItem.Visibility = showSongs ? Visibility.Visible : Visibility.Collapsed;
        if (showInAlbumsItem != null)
            showInAlbumsItem.Visibility = showAlbums ? Visibility.Visible : Visibility.Collapsed;
        if (showInQueueItem != null)
            showInQueueItem.Visibility = showQueue ? Visibility.Visible : Visibility.Collapsed;
    }
}
