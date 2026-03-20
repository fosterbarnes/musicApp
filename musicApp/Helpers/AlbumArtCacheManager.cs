using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using ATL;

namespace MusicApp.Helpers;

/// <summary>
/// Manages a persistent on-disk thumbnail cache for album art.
/// Thumbnails are stored as JPEG files keyed by album+artist hash under %AppData%\musicApp\thumbnails\.
/// </summary>
public static class AlbumArtCacheManager
{
    public const int CacheThumbnailSize = 250;

    private static readonly string ThumbnailFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "musicApp", "thumbnails");

    private static readonly ConcurrentDictionary<string, BitmapImage?> _memoryCache = new();

    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };

    static AlbumArtCacheManager()
    {
        try
        {
            Directory.CreateDirectory(ThumbnailFolder);
        }
        catch
        {
            // Swallow: cache will simply miss; methods handle missing folder gracefully.
        }
    }

    /// <summary>Returns the deterministic cache file path for an album+artist pair.</summary>
    public static string GetCachedPath(string album, string artist)
    {
        var key = (album ?? "").Trim().ToLowerInvariant() + "|" + (artist ?? "").Trim().ToLowerInvariant();
        var hashBytes = SHA1.HashData(Encoding.UTF8.GetBytes(key));
        var hex = Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
        return Path.Combine(ThumbnailFolder, hex + ".jpg");
    }

    /// <summary>
    /// Loads a cached thumbnail as a frozen BitmapImage, with optional WPF-side decode scaling.
    /// Returns null if no cache file exists.
    /// </summary>
    public static BitmapImage? TryGetCached(string album, string artist, int decodePixelWidth = 0)
    {
        var path = GetCachedPath(album, artist);
        return LoadFromCachePath(path, decodePixelWidth);
    }

    /// <summary>
    /// Loads a cached thumbnail by its file path with optional WPF-side decode scaling.
    /// Uses an in-memory dictionary so the same file is only read from disk once per session.
    /// </summary>
    public static BitmapImage? LoadFromCachePath(string cachePath, int decodePixelWidth = 0)
    {
        if (string.IsNullOrEmpty(cachePath) || !File.Exists(cachePath))
            return null;

        var cacheKey = cachePath + "|" + decodePixelWidth;
        if (_memoryCache.TryGetValue(cacheKey, out var cached))
            return cached;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(cachePath, UriKind.Absolute);
            if (decodePixelWidth > 0)
                bmp.DecodePixelWidth = decodePixelWidth;
            bmp.EndInit();
            bmp.Freeze();

            _memoryCache[cacheKey] = bmp;
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts album art for the given track (ATL embedded -> directory fallback),
    /// scales to CacheThumbnailSize, and saves as JPEG to the cache folder.
    /// Skips work if the cache file already exists.
    /// Returns the cache file path, or empty string if no art was found.
    /// </summary>
    public static string GenerateAndCache(Song track)
    {
        if (track == null || string.IsNullOrWhiteSpace(track.FilePath))
            return "";

        var cachePath = GetCachedPath(track.Album, track.Artist);

        if (File.Exists(cachePath))
            return cachePath;

        try { Directory.CreateDirectory(ThumbnailFolder); } catch { return ""; }

        try
        {
            Bitmap? source = null;

            try
            {
                var atlTrack = new Track(track.FilePath);
                var pics = atlTrack.EmbeddedPictures;
                if (pics != null && pics.Count > 0)
                {
                    using var ms = new MemoryStream(pics[0].PictureData);
                    source = new Bitmap(ms);
                }
            }
            catch
            {
                // Fall through to directory search
            }

            if (source == null)
            {
                var dir = Path.GetDirectoryName(track.FilePath);
                if (dir != null && Directory.Exists(dir))
                {
                    var imageFiles = Directory.GetFiles(dir, "*.*")
                        .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                        .ToList();

                    var artFile = imageFiles.FirstOrDefault(f =>
                    {
                        var name = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                        return name.Contains("album") || name.Contains("cover") || name.Contains("art") || name.Contains("folder");
                    }) ?? imageFiles.FirstOrDefault();

                    if (artFile != null)
                        source = new Bitmap(artFile);
                }
            }

            if (source == null)
                return "";

            using (source)
            {
                SaveScaledJpeg(source, cachePath, CacheThumbnailSize);
            }

            return cachePath;
        }
        catch
        {
            return "";
        }
    }

    /// <summary>Deletes all cached thumbnails and clears the in-memory cache.</summary>
    public static void InvalidateAll()
    {
        _memoryCache.Clear();
        try
        {
            if (Directory.Exists(ThumbnailFolder))
            {
                foreach (var file in Directory.GetFiles(ThumbnailFolder, "*.jpg"))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }

    /// <summary>Deletes a single album's cached thumbnail.</summary>
    public static void InvalidateAlbum(string album, string artist)
    {
        var path = GetCachedPath(album, artist);

        var keysToRemove = _memoryCache.Keys
            .Where(k => k.StartsWith(path, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var key in keysToRemove)
            _memoryCache.TryRemove(key, out _);

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { }
    }

    /// <summary>Clears the in-memory bitmap cache without touching disk files.</summary>
    public static void ClearMemoryCache()
    {
        _memoryCache.Clear();
    }

    private static void SaveScaledJpeg(Bitmap original, string outputPath, int targetSize)
    {
        int w = original.Width;
        int h = original.Height;
        if (w <= 0 || h <= 0) return;

        double ratio = Math.Min((double)targetSize / w, (double)targetSize / h);
        int newW = Math.Max(1, (int)(w * ratio));
        int newH = Math.Max(1, (int)(h * ratio));

        using var scaled = new Bitmap(newW, newH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.DrawImage(original, 0, 0, newW, newH);
        }

        var jpegEncoder = ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(e => e.FormatID == ImageFormat.Jpeg.Guid);

        if (jpegEncoder != null)
        {
            var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
            scaled.Save(outputPath, jpegEncoder, encoderParams);
        }
        else
        {
            scaled.Save(outputPath, ImageFormat.Jpeg);
        }
    }
}
