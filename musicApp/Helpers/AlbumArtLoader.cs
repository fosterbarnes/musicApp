using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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

    public static BitmapImage? LoadAlbumArt(Song track)
    {
        try
        {
            if (!string.IsNullOrEmpty(track.ThumbnailCachePath))
            {
                var cached = AlbumArtCacheManager.LoadFromCachePath(track.ThumbnailCachePath);
                if (cached != null)
                    return cached;
            }

            try
            {
                var atlTrack = new Track(track.FilePath);
                var embeddedPictures = atlTrack.EmbeddedPictures;

                if (embeddedPictures != null && embeddedPictures.Count > 0)
                {
                    return CreateScaledImage(embeddedPictures[0].PictureData);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading embedded album art for {track.Title}: {ex.Message}");
            }

            var directory = Path.GetDirectoryName(track.FilePath);
            if (directory == null)
            {
                return null;
            }

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

            return albumArtFile != null ? CreateScaledImageFromFile(albumArtFile) : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading album art for {track.Title}: {ex.Message}");
            return null;
        }
    }

    private static BitmapImage? CreateScaledImage(byte[] imageData)
    {
        try
        {
            using var originalStream = new MemoryStream(imageData);
            using var originalBitmap = new Bitmap(originalStream);
            return ScaleBitmapToWpfImage(originalBitmap);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error creating high-quality scaled image: {ex.Message}");
            return null;
        }
    }

    private static BitmapImage? CreateScaledImageFromFile(string filePath)
    {
        try
        {
            using var originalBitmap = new Bitmap(filePath);
            return ScaleBitmapToWpfImage(originalBitmap);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error creating high-quality scaled image from file: {ex.Message}");
            return null;
        }
    }

    private static BitmapImage ScaleBitmapToWpfImage(Bitmap originalBitmap)
    {
        int targetSize = UILayoutConstants.TitleBarAlbumArtRenderSize;
        int originalWidth = originalBitmap.Width;
        int originalHeight = originalBitmap.Height;

        double ratio = Math.Min((double)targetSize / originalWidth, (double)targetSize / originalHeight);
        int newWidth = (int)(originalWidth * ratio);
        int newHeight = (int)(originalHeight * ratio);

        using var scaledBitmap = new Bitmap(newWidth, newHeight, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(scaledBitmap))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.DrawImage(originalBitmap, 0, 0, newWidth, newHeight);
        }

        var wpfBitmap = new BitmapImage();
        using var stream = new MemoryStream();
        scaledBitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        wpfBitmap.BeginInit();
        wpfBitmap.CacheOption = BitmapCacheOption.OnLoad;
        wpfBitmap.StreamSource = stream;
        wpfBitmap.EndInit();
        wpfBitmap.Freeze();

        return wpfBitmap;
    }
}
