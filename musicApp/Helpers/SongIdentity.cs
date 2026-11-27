using System;
using System.Collections;
using System.Collections.Generic;

namespace musicApp.Helpers;

public static class SongIdentity
{
    public static bool SamePath(Song? a, Song? b)
    {
        if (a == null || b == null)
            return ReferenceEquals(a, b);
        if (!string.IsNullOrWhiteSpace(a.FilePath) && !string.IsNullOrWhiteSpace(b.FilePath))
            return string.Equals(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase);
        return ReferenceEquals(a, b);
    }

    public static bool Matches(Song? a, Song? b)
    {
        if (a == null || b == null)
            return ReferenceEquals(a, b);
        if (ReferenceEquals(a, b))
            return true;
        // Two different files are never the same song: untagged files all read "Unknown Title/Artist/Album",
        // so a tag match would treat them as one. Tags only identify a song that has no path.
        if (!string.IsNullOrWhiteSpace(a.FilePath) && !string.IsNullOrWhiteSpace(b.FilePath))
            return string.Equals(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase);
        return string.Equals(a.Title, b.Title, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(a.Artist, b.Artist, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(a.Album, b.Album, StringComparison.OrdinalIgnoreCase);
    }

    public static int IndexOf(IEnumerable<Song>? list, Song? target)
    {
        if (list == null || target == null)
            return -1;

        int i = 0;
        int pathHit = -1;
        int metaHit = -1;
        foreach (var item in list)
        {
            if (ReferenceEquals(item, target))
                return i;
            if (pathHit < 0 &&
                !string.IsNullOrWhiteSpace(target.FilePath) &&
                item != null &&
                string.Equals(item.FilePath, target.FilePath, StringComparison.OrdinalIgnoreCase))
                pathHit = i;
            if (metaHit < 0 && Matches(item, target))
                metaHit = i;
            i++;
        }

        if (pathHit >= 0)
            return pathHit;
        return metaHit;
    }

    public static int IndexOfByPath(IList<Song> list, Song target)
    {
        if (list == null || target == null)
            return -1;
        for (int i = 0; i < list.Count; i++)
        {
            if (SamePath(list[i], target))
                return i;
        }
        return -1;
    }

    public static Song? FindInEnumerable(IEnumerable? items, Song? target)
    {
        if (items == null || target == null)
            return null;

        Song? fallback = null;
        foreach (var item in items)
        {
            if (item is not Song song)
                continue;
            if (ReferenceEquals(song, target))
                return song;
            if (!string.IsNullOrWhiteSpace(song.FilePath) &&
                !string.IsNullOrWhiteSpace(target.FilePath) &&
                string.Equals(song.FilePath, target.FilePath, StringComparison.OrdinalIgnoreCase))
                return song;
            if (fallback == null && Matches(song, target))
                fallback = song;
        }

        return fallback;
    }
}
