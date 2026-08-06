using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ATL;
using PhotoSauce.MagicScaler;

namespace musicApp.Helpers;

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

    private const int MemoryCacheMaxEntries = 512;
    private static readonly ConcurrentDictionary<string, BitmapSource?> _memoryCache = new();
    private static readonly ConcurrentDictionary<string, byte> _noArtworkCache = new();

    /// <summary>Limits parallel on-disk thumbnail generation (decode + MagicScaler + JPEG encode).</summary>
    private static readonly SemaphoreSlim ThumbnailGenerationSemaphore = new(2, 2);

    private static readonly ConcurrentDictionary<string, object> ThumbnailPathLocks = new();

    private static void TrimMemoryCacheIfNeeded()
    {
        if (_memoryCache.Count <= MemoryCacheMaxEntries) return;
        _memoryCache.Clear();
    }

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

    /// <summary>Artist slice used for thumbnail filenames; matches album grid grouping (album artist when set).</summary>
    public static string GetThumbnailCacheArtistKey(Song? track)
    {
        if (track == null)
            return "";
        return !string.IsNullOrWhiteSpace(track.AlbumArtist)
            ? track.AlbumArtist
            : track.Artist ?? "";
    }

    public static string GetCachedPathForTrack(Song track) =>
        GetCachedPath(track.Album ?? "", GetThumbnailCacheArtistKey(track));

    /// <summary>
    /// Loads a cached thumbnail as a frozen BitmapImage, with optional WPF-side decode scaling.
    /// Returns null if no cache file exists.
    /// </summary>
    public static BitmapSource? TryGetCached(string album, string artist, int decodePixelWidth = 0)
    {
        var path = GetCachedPath(album, artist);
        return LoadFromCachePath(path, decodePixelWidth);
    }

    /// <summary>
    /// Loads a cached thumbnail by its file path with optional WPF-side decode scaling.
    /// Uses an in-memory dictionary so the same file is only read from disk once per session.
    /// </summary>
    public static BitmapSource? LoadFromCachePath(string cachePath, int decodePixelWidth = 0)
    {
        if (string.IsNullOrEmpty(cachePath))
            return null;

        var cacheKey = cachePath + "|" + decodePixelWidth;
        if (_memoryCache.TryGetValue(cacheKey, out var cached))
            return cached;
        if (!File.Exists(cachePath))
            return null;

        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(cachePath);
                if (bytes.Length < 24 || !LooksLikeRasterImageHeader(bytes))
                {
                    if (attempt < 3)
                    {
                        Thread.Sleep(25);
                        continue;
                    }
                    return null;
                }

                using var ms = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                if (decodePixelWidth > 0)
                    bmp.DecodePixelWidth = decodePixelWidth;
                bmp.EndInit();

                if (bmp.PixelWidth <= 0 || bmp.PixelHeight <= 0)
                {
                    if (attempt < 3)
                    {
                        Thread.Sleep(25);
                        continue;
                    }
                    return null;
                }

                bmp.Freeze();
                _memoryCache[cacheKey] = bmp;
                TrimMemoryCacheIfNeeded();
                return bmp;
            }
            catch
            {
                if (attempt < 3)
                    Thread.Sleep(25);
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts album art for the given track (ATL embedded, then images beside the file),
    /// scales to CacheThumbnailSize, and saves as JPEG to the cache folder.
    /// Skips work if the cache file already exists.
    /// Returns the cache file path, or empty string if no art was found.
    /// </summary>
    public static string GenerateAndCache(Song track)
    {
        if (track == null || string.IsNullOrWhiteSpace(track.FilePath))
            return "";

        var cachePath = GetCachedPathForTrack(track);

        if (!string.Equals(track.AlbumArtPath, "embedded", StringComparison.OrdinalIgnoreCase) &&
            _noArtworkCache.ContainsKey(cachePath))
        {
            return "";
        }

        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        try { Directory.CreateDirectory(ThumbnailFolder); } catch { return ""; }

        var pathLock = ThumbnailPathLocks.GetOrAdd(cachePath, _ => new object());
        lock (pathLock)
        {
            if (File.Exists(cachePath))
                return cachePath;

            ThumbnailGenerationSemaphore.Wait();
            try
            {
                if (File.Exists(cachePath))
                    return cachePath;

                Bitmap? source = null;

                try
                {
                    var embedded = AlbumArtSourceResolver.TryLoadEmbeddedBytes(track.FilePath);
                    if (embedded != null)
                        source = TryLoadBitmapFromPictureBytes(embedded);
                }
                catch
                {
                }

                if (source == null)
                {
                    var artFile = AlbumArtSourceResolver.TryFindDirectoryCoverPath(track.FilePath);
                    if (artFile != null)
                        source = TryLoadBitmapFromImageFile(artFile);
                }

                if (source == null)
                {
                    _noArtworkCache.TryAdd(cachePath, 0);
                    return "";
                }

                using (source)
                {
                    if (!TrySaveScaledJpeg(source, cachePath, CacheThumbnailSize))
                        return "";
                }

                return cachePath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AlbumArtCacheManager.GenerateAndCache: {ex.Message}");
                return "";
            }
            finally
            {
                ThumbnailGenerationSemaphore.Release();
            }
        }
    }

    /// <summary>Deletes all cached thumbnails and clears the in-memory cache.</summary>
    public static void InvalidateAll()
    {
        AlbumArtThumbnailHelper.ClearFullSizeCache();
        AlbumArtSourceResolver.ClearDirectoryCoverCache();
        _memoryCache.Clear();
        _noArtworkCache.Clear();
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
        var pathLock = ThumbnailPathLocks.GetOrAdd(path, _ => new object());
        lock (pathLock)
        {
            InvalidateCachedPath(path);
        }
    }

    private static void InvalidateCachedPath(string path)
    {
        _noArtworkCache.TryRemove(path, out _);
        AlbumArtSourceResolver.ClearDirectoryCoverCache();

        var keysToRemove = _memoryCache.Keys
            .Where(k => k.StartsWith(path, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var key in keysToRemove)
            _memoryCache.TryRemove(key, out _);

        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    /// <summary>Clears the in-memory bitmap cache without touching disk files.</summary>
    public static void ClearMemoryCache()
    {
        AlbumArtThumbnailHelper.ClearFullSizeCache();
        _memoryCache.Clear();
        _noArtworkCache.Clear();
    }

    private static bool LooksLikeRasterImageHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2) return false;
        if (data[0] == 0xFF && data[1] == 0xD8) return true;
        if (data.Length >= 4 && data[0] == 0x89 && data[1] == 'P' && data[2] == 'N' && data[3] == 'G')
            return true;
        if (data.Length >= 3 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F') return true;
        if (data.Length >= 2 && data[0] == 'B' && data[1] == 'M') return true;
        return false;
    }

    private static Bitmap? TryLoadBitmapFromPictureBytes(byte[]? data)
    {
        if (data == null || data.Length < 24 || !LooksLikeRasterImageHeader(data))
            return null;

        try
        {
            using var ms = new MemoryStream(data, 0, data.Length, writable: false, publiclyVisible: true);
            using var img = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false);
            return new Bitmap(img);
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? TryLoadBitmapFromImageFile(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length < 24) return null;

            Span<byte> head = stackalloc byte[12];
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (fs.Read(head) < 2) return null;
            }

            if (!LooksLikeRasterImageHeader(head))
                return null;

            return new Bitmap(path);
        }
        catch
        {
            return null;
        }
    }

    private const int MaxAlbumArtDimensionPx = 8192;

    private static Bitmap? TryFlattenTo32bppArgb(Image src)
    {
        try
        {
            int w = src.Width;
            int h = src.Height;
            if (w < 1 || h < 1 || w > MaxAlbumArtDimensionPx || h > MaxAlbumArtDimensionPx)
                return null;

            var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.White);
                g.DrawImage(src, 0, 0, w, h);
            }

            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static bool TrySaveScaledJpeg(Bitmap original, string outputPath, int targetSize)
    {
        try
        {
            using var flat = TryFlattenTo32bppArgb(original);
            if (flat == null)
                return false;

            // PNG in memory: WIC-friendly container for MagicScaler input (not for on-screen display).
            using var pngMs = new MemoryStream();
            flat.Save(pngMs, ImageFormat.Png);
            pngMs.Position = 0;
            var imageInfo = ImageFileInfo.Load(pngMs);
            pngMs.Position = 0;
            var settings = new ProcessImageSettings
            {
                Width = targetSize,
                Height = targetSize,
                ResizeMode = CropScaleMode.Max,
                DpiX = 96,
                DpiY = 96,
                EncoderOptions = new JpegEncoderOptions(90, ChromaSubsampleMode.Default, false),
            };
            settings = ProcessImageSettings.Calculate(settings, imageInfo);
            settings.TrySetEncoderFormat(ImageMimeTypes.Jpeg);
            pngMs.Position = 0;

            var tmpPath = outputPath + ".writing";
            try
            {
                MagicImageProcessor.ProcessImage(pngMs, tmpPath, settings);
                File.Move(tmpPath, outputPath, overwrite: true);
            }
            catch
            {
                TryDeleteFileSilent(tmpPath);
                TryDeleteFileSilent(outputPath);
                return false;
            }

            return true;
        }
        catch
        {
            TryDeleteFileSilent(outputPath);
            return false;
        }
    }

    private static void TryDeleteFileSilent(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
