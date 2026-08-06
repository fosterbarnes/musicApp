using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using ATL;
using musicApp;
using musicApp.Constants;

namespace musicApp.Helpers;

public static class AlbumArtLoader
{
    /// <summary>Physical pixel size for the title bar album square at the given DPI (matches <see cref="UILayoutConstants.TitleBarAlbumArtLogicalSizeDip"/> dip).</summary>
    public static int GetTitleBarTargetPixelSize(System.Windows.DpiScale dpi)
    {
        double scale = Math.Max(dpi.DpiScaleX, dpi.DpiScaleY);
        return Math.Max(1, (int)Math.Ceiling(UILayoutConstants.TitleBarAlbumArtLogicalSizeDip * scale));
    }

    /// <summary>Loads art for the title bar; <paramref name="targetSizePx"/> sets JPEG decode width and downscale target.</summary>
    public static BitmapSource? LoadAlbumArt(Song track, int targetSizePx)
    {
        try
        {
            if (targetSizePx < 1)
                targetSizePx = (int)Math.Ceiling(UILayoutConstants.TitleBarAlbumArtLogicalSizeDip);

            if (!string.IsNullOrEmpty(track.ThumbnailCachePath))
            {
                var cached = AlbumArtCacheManager.LoadFromCachePath(track.ThumbnailCachePath, targetSizePx);
                if (cached != null)
                    return cached;
            }

            var artist = AlbumArtCacheManager.GetThumbnailCacheArtistKey(track);
            var byAlbum = AlbumArtCacheManager.TryGetCached(track.Album ?? "", artist, targetSizePx);
            if (byAlbum != null)
                return byAlbum;
            if (!string.Equals(artist, track.Artist ?? "", StringComparison.Ordinal))
            {
                var legacy = AlbumArtCacheManager.TryGetCached(track.Album ?? "", track.Artist ?? "", targetSizePx);
                if (legacy != null)
                    return legacy;
            }

            try
            {
                var embedded = AlbumArtSourceResolver.TryLoadEmbeddedBytes(track.FilePath);
                if (embedded != null)
                {
                    var bmp = AlbumArtDownscaleHelper.TryDownscaleToBitmapSource(embedded, targetSizePx);
                    if (bmp != null)
                        return bmp;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading embedded album art for {track.Title}: {ex.Message}");
            }

            var albumArtFile = AlbumArtSourceResolver.TryFindDirectoryCoverPath(track.FilePath);
            if (albumArtFile != null)
                return AlbumArtDownscaleHelper.TryDownscaleToBitmapSource(albumArtFile, targetSizePx);

            var itcBytes = AlbumArtSourceResolver.TryLoadItunesCacheBytes(track.FilePath);
            return itcBytes != null ? AlbumArtDownscaleHelper.TryDownscaleToBitmapSource(itcBytes, targetSizePx) : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading album art for {track.Title}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Loads at 96 DPI logical size (50 px); prefer <see cref="LoadAlbumArt(Song, int)"/> with DPI-aware size from the title bar.</summary>
    public static BitmapSource? LoadAlbumArt(Song track) =>
        LoadAlbumArt(track, (int)Math.Ceiling(UILayoutConstants.TitleBarAlbumArtLogicalSizeDip));
}
