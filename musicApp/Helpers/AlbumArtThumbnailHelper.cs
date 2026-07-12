using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using ATL;

namespace musicApp.Helpers;

/// <summary>Loads and scales album art for thumbnails (e.g. search popup, lists). Uses same logic as title bar: embedded art via ATL, then directory fallback.</summary>
public static class AlbumArtThumbnailHelper
{
    /// <summary>Default thumbnail size in pixels (for 40px display at 2x DPI).</summary>
    public const int DefaultThumbnailSize = 80;
    private const int FullSizeCacheMaxEntries = 96;
    private static readonly ConcurrentDictionary<string, BitmapImage?> _fullSizeCache = new();

    public static void ClearFullSizeCache() => _fullSizeCache.Clear();

    private static void TrimFullSizeCacheIfNeeded()
    {
        if (_fullSizeCache.Count <= FullSizeCacheMaxEntries) return;
        _fullSizeCache.Clear();
    }

    /// <summary>Load album art for a track and scale to the given size. Returns null if none found.</summary>
    public static BitmapSource? LoadForTrack(Song track, int targetSizePx = DefaultThumbnailSize)
    {
        if (track == null || string.IsNullOrWhiteSpace(track.FilePath))
            return null;

        // Fast path: load from on-disk thumbnail cache and decode near target size.
        if (!string.IsNullOrEmpty(track.ThumbnailCachePath))
        {
            var decodeWidth = targetSizePx > 0 ? Math.Max(targetSizePx, 64) : 0;
            var cached = AlbumArtCacheManager.LoadFromCachePath(track.ThumbnailCachePath, decodeWidth);
            if (cached != null)
                return cached;
        }

        var decodePx = targetSizePx > 0 ? Math.Max(targetSizePx, 64) : 0;
        var canonArtist = AlbumArtCacheManager.GetThumbnailCacheArtistKey(track);
        var fromAlbumKey = AlbumArtCacheManager.TryGetCached(track.Album ?? "", canonArtist, decodePx);
        if (fromAlbumKey != null)
            return fromAlbumKey;
        if (!string.Equals(canonArtist, track.Artist ?? "", StringComparison.Ordinal))
        {
            var legacy = AlbumArtCacheManager.TryGetCached(track.Album ?? "", track.Artist ?? "", decodePx);
            if (legacy != null)
                return legacy;
        }

        try
        {
            var embedded = AlbumArtSourceResolver.TryLoadEmbeddedBytes(track.FilePath);
            if (embedded != null)
                return AlbumArtDownscaleHelper.TryDownscaleToBitmapSource(embedded, targetSizePx);

            var albumArtFile = AlbumArtSourceResolver.TryFindDirectoryCoverPath(track.FilePath);
            if (albumArtFile != null)
                return AlbumArtDownscaleHelper.TryDownscaleToBitmapSource(albumArtFile, targetSizePx);

            var itcBytes = AlbumArtSourceResolver.TryLoadItunesCacheBytes(track.FilePath);
            if (itcBytes != null)
                return AlbumArtDownscaleHelper.TryDownscaleToBitmapSource(itcBytes, targetSizePx);

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Loads full-resolution album art for a selected album flyout.
    /// Uses a memory cache keyed by file path to avoid repeated decoding.
    /// </summary>
    public static BitmapImage? LoadFullSizeForTrack(Song track)
    {
        if (track == null || string.IsNullOrWhiteSpace(track.FilePath))
            return null;

        var key = track.FilePath;
        if (_fullSizeCache.TryGetValue(key, out var cached))
            return cached;

        BitmapImage? result = null;

        try
        {
            var embedded = AlbumArtSourceResolver.TryLoadEmbeddedBytes(track.FilePath);
            if (embedded != null)
                result = LoadBitmapImageFromBytes(embedded);

            if (result == null)
            {
                var albumArtFile = AlbumArtSourceResolver.TryFindDirectoryCoverPath(track.FilePath);
                if (albumArtFile != null)
                    result = LoadBitmapImageFromFile(albumArtFile);
            }

            if (result == null)
            {
                var itcBytes = AlbumArtSourceResolver.TryLoadItunesCacheBytes(track.FilePath);
                if (itcBytes != null)
                    result = LoadBitmapImageFromBytes(itcBytes);
            }
        }
        catch
        {
            result = null;
        }

        _fullSizeCache[key] = result;
        TrimFullSizeCacheIfNeeded();
        return result;
    }

    public static void InvalidateFullSizeCache(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return;
        _fullSizeCache.TryRemove(filePath, out _);
    }

    private static BitmapImage? LoadBitmapImageFromBytes(byte[] imageData)
    {
        if (imageData == null || imageData.Length == 0)
            return null;

        try
        {
            var bitmap = new BitmapImage();
            using (var stream = new MemoryStream(imageData, 0, imageData.Length, writable: false, publiclyVisible: true))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }
            if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
                return null;
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapImage? LoadBitmapImageFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
