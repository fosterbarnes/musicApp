using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace musicApp.Helpers;

/// <summary>
/// Decoded album grid thumbnails, one entry per album, kept under <see cref="BudgetBytes"/> of decoded pixels by
/// dropping the least recently used album. Keyed by the album's disk thumbnail path, so Albums and Recently Added
/// (different representative tracks) share one decode. Thread-safe; on a miss <see cref="GetOrLoad"/> decodes on
/// the calling thread. Albums with no art are remembered as null until <see cref="ForgetMissing"/>, so a tile does
/// not re-probe the file on every request.
/// </summary>
public static class AlbumGridArtCache
{
    /// <summary>
    /// ~1,900 albums at the default 158 px tile, ~1,000 at the largest (220 px). A budget below the library's
    /// art made every Albums / Recently Added switch evict and re-decode hundreds of albums (measured).
    /// </summary>
    public const long BudgetBytes = 192L * 1024 * 1024;

    private sealed record Entry(string Key, int Px, BitmapSource? Image)
    {
        public long Bytes { get; } = Image == null ? 0 : (long)Image.PixelWidth * Image.PixelHeight * 4;
    }

    private static readonly object _lock = new();
    private static readonly Dictionary<string, LinkedListNode<Entry>> _map = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<Entry> _recent = new(); // first = most recently used
    private static long _bytes;

    /// <summary>
    /// Art for <paramref name="track"/>'s album at <paramref name="px"/>, decoding it now on a miss.
    /// <paramref name="key"/> is the album's <c>AlbumArtCacheManager.GetCachedPathForTrack</c>.
    /// </summary>
    public static BitmapSource? GetOrLoad(string key, Song track, int px)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var hit) && hit.Value.Px == px)
            {
                _recent.Remove(hit);
                _recent.AddFirst(hit);
                return hit.Value.Image;
            }
        }

        var image = ToRenderFormat(AlbumArtThumbnailHelper.LoadForTrack(track, px, keepInMemory: false));
        lock (_lock)
        {
            RemoveLocked(key);
            var node = _recent.AddFirst(new Entry(key, px, image));
            _map[key] = node;
            _bytes += node.Value.Bytes;
            // A displayed tile keeps its bitmap alive even when evicted; eviction frees the rest.
            while (_bytes > BudgetBytes && _recent.Last is { } oldest && oldest != node)
                RemoveLocked(oldest.Value.Key);
        }
        return image;
    }

    /// <summary>How many albums of <paramref name="px"/> art a prefetch may fill without evicting the view.</summary>
    public static int AlbumsInBudget(int px) => (int)(BudgetBytes * 3 / 4 / Math.Max(1L, 4L * px * px));

    /// <summary>Drops one album; <paramref name="cachePath"/> is its disk thumbnail path.</summary>
    public static void Invalidate(string cachePath)
    {
        lock (_lock)
            RemoveLocked(cachePath);
    }

    public static void Clear()
    {
        lock (_lock)
        {
            _map.Clear();
            _recent.Clear();
            _bytes = 0;
        }
    }

    /// <summary>Retries albums that had no art (a rescan or new cover file may have added some).</summary>
    public static void ForgetMissing()
    {
        lock (_lock)
        {
            for (var node = _recent.First; node != null;)
            {
                var next = node.Next;
                if (node.Value.Image == null)
                    RemoveLocked(node.Value.Key);
                node = next;
            }
        }
    }

    /// <summary>
    /// WPF draws Pbgra32. A JPEG decodes to Bgr32, so the first draw made a converted copy that lived as long as
    /// the bitmap. Converting once here keeps one buffer per album.
    /// </summary>
    private static BitmapSource? ToRenderFormat(BitmapSource? image)
    {
        if (image == null || image.Format == PixelFormats.Pbgra32)
            return image;
        var converted = new FormatConvertedBitmap(image, PixelFormats.Pbgra32, null, 0);
        int stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var result = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    private static void RemoveLocked(string key)
    {
        if (!_map.Remove(key, out var node))
            return;
        _recent.Remove(node);
        _bytes -= node.Value.Bytes;
    }
}
