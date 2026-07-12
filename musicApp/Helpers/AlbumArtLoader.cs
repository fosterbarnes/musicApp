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
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };

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

            try
            {
                using var fs = new FileStream(track.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var atlTrack = new Track(fs);
                var embeddedPictures = atlTrack.EmbeddedPictures;

                if (embeddedPictures != null && embeddedPictures.Count > 0)
                {
                    var bmp = AlbumArtDownscaleHelper.TryDownscaleToBitmapSource(embeddedPictures[0].PictureData, targetSizePx);
                    if (bmp != null)
                        return bmp;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading embedded album art for {track.Title}: {ex.Message}");
            }

            var directory = Path.GetDirectoryName(track.FilePath);
            if (directory == null)
                return null;

            var imageFiles = Directory.GetFiles(directory, "*.*")
                .Where(file => ImageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                .ToList();

            var albumArtFile = imageFiles.FirstOrDefault(file =>
            {
                var fileName = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                return fileName.Contains("album") ||
                       fileName.Contains("cover") ||
                       fileName.Contains("art") ||
                       fileName.Contains("folder");
            }) ?? imageFiles.FirstOrDefault();

            if (albumArtFile != null)
                return AlbumArtDownscaleHelper.TryDownscaleToBitmapSource(albumArtFile, targetSizePx);

            var itcBytes = FruitAppLocalAlbumArtCache.TryGetCoverImageBytesForAudioPath(track.FilePath);
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
