using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using ATL;

namespace MusicApp.Helpers;

/// <summary>Loads and scales album art for thumbnails (e.g. search popup, lists). Uses same logic as title bar: embedded art via ATL, then directory fallback.</summary>
public static class AlbumArtThumbnailHelper
{
    /// <summary>Default thumbnail size in pixels (for 40px display at 2x DPI).</summary>
    public const int DefaultThumbnailSize = 80;

    /// <summary>Load album art for a track and scale to the given size. Returns null if none found.</summary>
    public static BitmapImage? LoadForTrack(Song track, int targetSizePx = DefaultThumbnailSize)
    {
        if (track == null || string.IsNullOrWhiteSpace(track.FilePath))
            return null;

        try
        {
            // Embedded art via ATL
            try
            {
                var atlTrack = new Track(track.FilePath);
                var embeddedPictures = atlTrack.EmbeddedPictures;

                if (embeddedPictures != null && embeddedPictures.Count > 0)
                {
                    var picture = embeddedPictures[0];
                    return ScaleToBitmapImage(picture.PictureData, targetSizePx);
                }
            }
            catch
            {
                // Fall through to directory search
            }

            // Fallback: image in same directory as file
            var directory = Path.GetDirectoryName(track.FilePath);
            if (directory == null || !Directory.Exists(directory))
                return null;

            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
            var imageFiles = Directory.GetFiles(directory, "*.*")
                .Where(f => imageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();

            var albumArtFile = imageFiles.FirstOrDefault(file =>
            {
                var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                return name.Contains("album") || name.Contains("cover") || name.Contains("art") || name.Contains("folder");
            }) ?? imageFiles.FirstOrDefault();

            if (albumArtFile != null)
                return ScaleToBitmapImageFromFile(albumArtFile, targetSizePx);

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Scale raw image data to a square-ish thumbnail and return as frozen WPF BitmapImage.</summary>
    public static BitmapImage? ScaleToBitmapImage(byte[] imageData, int targetSizePx)
    {
        if (imageData == null || imageData.Length == 0) return null;
        try
        {
            using var ms = new MemoryStream(imageData);
            using var bmp = new Bitmap(ms);
            return ScaleToWpfBitmapImage(bmp, targetSizePx);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Load a file and scale to thumbnail.</summary>
    public static BitmapImage? ScaleToBitmapImageFromFile(string filePath, int targetSizePx)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;
        try
        {
            using var bmp = new Bitmap(filePath);
            return ScaleToWpfBitmapImage(bmp, targetSizePx);
        }
        catch
        {
            return null;
        }
    }

    private static BitmapImage? ScaleToWpfBitmapImage(Bitmap originalBitmap, int targetSizePx)
    {
        try
        {
            int w = originalBitmap.Width;
            int h = originalBitmap.Height;
            if (w <= 0 || h <= 0) return null;

            double ratio = Math.Min((double)targetSizePx / w, (double)targetSizePx / h);
            int newW = (int)(w * ratio);
            int newH = (int)(h * ratio);
            if (newW <= 0 || newH <= 0) return null;

            using var scaled = new Bitmap(newW, newH, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(scaled);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.DrawImage(originalBitmap, 0, 0, newW, newH);

            var wpfBitmap = new BitmapImage();
            using var stream = new MemoryStream();
            scaled.Save(stream, ImageFormat.Png);
            stream.Position = 0;

            wpfBitmap.BeginInit();
            wpfBitmap.CacheOption = BitmapCacheOption.OnLoad;
            wpfBitmap.StreamSource = stream;
            wpfBitmap.EndInit();
            wpfBitmap.Freeze();
            return wpfBitmap;
        }
        catch
        {
            return null;
        }
    }
}
