using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Media.Imaging;
using ATL;

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
    private static readonly ConcurrentDictionary<string, BitmapImage?> _memoryCache = new();

    /// <summary>Limits parallel album-art work; GDI+ is fragile under heavy parallel load.</summary>
    private static readonly SemaphoreSlim ThumbnailGdiSemaphore = new(2, 2);

    private static readonly ConcurrentDictionary<string, object> ThumbnailPathLocks = new();

    private static readonly object JpegEncodeLock = new();

    private static void TrimMemoryCacheIfNeeded()
    {
        if (_memoryCache.Count <= MemoryCacheMaxEntries) return;
        _memoryCache.Clear();
    }

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

        var cachePath = GetCachedPath(track.Album, track.Artist);

        if (File.Exists(cachePath))
            return cachePath;

        try { Directory.CreateDirectory(ThumbnailFolder); } catch { return ""; }

        var pathLock = ThumbnailPathLocks.GetOrAdd(cachePath, _ => new object());
        lock (pathLock)
        {
            if (File.Exists(cachePath))
                return cachePath;

            ThumbnailGdiSemaphore.Wait();
            try
            {
                if (File.Exists(cachePath))
                    return cachePath;

                Bitmap? source = null;

                try
                {
                    var atlTrack = new Track(track.FilePath);
                    var pics = atlTrack.EmbeddedPictures;
                    if (pics != null && pics.Count > 0)
                        source = TryLoadBitmapFromPictureBytes(pics[0].PictureData);
                }
                catch
                {
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
                            source = TryLoadBitmapFromImageFile(artFile);
                    }
                }

                if (source == null)
                    return "";

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
                ThumbnailGdiSemaphore.Release();
            }
        }
    }

    /// <summary>Deletes all cached thumbnails and clears the in-memory cache.</summary>
    public static void InvalidateAll()
    {
        AlbumArtThumbnailHelper.ClearFullSizeCache();
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
        AlbumArtThumbnailHelper.ClearFullSizeCache();
        _memoryCache.Clear();
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

            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
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

            int w = flat.Width;
            int h = flat.Height;
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
                g.DrawImage(flat, 0, 0, newW, newH);
            }

            byte[] jpegBytes;
            lock (JpegEncodeLock)
            {
                using var ms = new MemoryStream();
                var jpegEncoder = ImageCodecInfo.GetImageEncoders()
                    .FirstOrDefault(e => e.FormatID == ImageFormat.Jpeg.Guid);

                if (jpegEncoder != null)
                {
                    using var encoderParams = new EncoderParameters(1);
                    encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
                    scaled.Save(ms, jpegEncoder, encoderParams);
                }
                else
                    scaled.Save(ms, ImageFormat.Jpeg);

                jpegBytes = ms.ToArray();
            }

            var tmpPath = outputPath + ".writing";
            try
            {
                File.WriteAllBytes(tmpPath, jpegBytes);
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
