using System.Drawing;
using System.IO;
using System.Windows.Media.Imaging;
using PhotoSauce.MagicScaler;
using MsPixelFormats = PhotoSauce.MagicScaler.PixelFormats;

namespace musicApp.Helpers;

/// <summary>Downscales album art with MagicScaler and returns a frozen <see cref="BitmapSource"/> (no PNG round-trip for display).</summary>
public static class AlbumArtDownscaleHelper
{
    /// <summary>Fit inside a square of <paramref name="targetSizePx"/> on the long side; never upscale.</summary>
    public static BitmapSource? TryDownscaleToBitmapSource(byte[]? imageData, int targetSizePx)
    {
        if (imageData == null || imageData.Length == 0 || targetSizePx < 1)
            return null;
        try
        {
            using var stream = new MemoryStream(imageData, 0, imageData.Length, writable: false, publiclyVisible: true);
            var scaled = TryDownscaleMagicScaler(stream, targetSizePx);
            if (scaled != null)
                return scaled;
        }
        catch
        {
        }

        // MagicScaler can fail on UI-thread WIC/STA or odd embeds; WPF decode matches flyout path.
        return TryWpfDecodeScaled(imageData, targetSizePx);
    }

    /// <inheritdoc cref="TryDownscaleToBitmapSource(byte[]?, int)"/>
    public static BitmapSource? TryDownscaleToBitmapSource(string filePath, int targetSizePx)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) || targetSizePx < 1)
            return null;
        try
        {
            var imageInfo = ImageFileInfo.Load(filePath);
            var settings = BaseSettings(targetSizePx);
            settings = ProcessImageSettings.Calculate(settings, imageInfo);
            using var pipeline = MagicImageProcessor.BuildPipeline(filePath, settings);
            var scaled = CopyPixelSourceToFrozenBitmap(pipeline.PixelSource);
            if (scaled != null)
                return scaled;
        }
        catch
        {
        }

        try
        {
            var bytes = File.ReadAllBytes(filePath);
            return TryWpfDecodeScaled(bytes, targetSizePx);
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? TryDownscaleMagicScaler(Stream stream, int targetSizePx)
    {
        if (!stream.CanSeek)
            return null;
        var imageInfo = ImageFileInfo.Load(stream);
        stream.Position = 0;
        var settings = BaseSettings(targetSizePx);
        settings = ProcessImageSettings.Calculate(settings, imageInfo);
        stream.Position = 0;
        using var pipeline = MagicImageProcessor.BuildPipeline(stream, settings);
        return CopyPixelSourceToFrozenBitmap(pipeline.PixelSource);
    }

    private static BitmapSource? TryWpfDecodeScaled(byte[] imageData, int targetSizePx)
    {
        try
        {
            var bitmap = new BitmapImage();
            using (var stream = new MemoryStream(imageData, 0, imageData.Length, writable: false, publiclyVisible: true))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                if (targetSizePx > 0)
                    bitmap.DecodePixelWidth = targetSizePx;
                bitmap.EndInit();
            }

            if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
                return null;
            if (bitmap.CanFreeze)
                bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static ProcessImageSettings BaseSettings(int targetSizePx) => new()
    {
        Width = targetSizePx,
        Height = targetSizePx,
        ResizeMode = CropScaleMode.Max,
        DpiX = 96,
        DpiY = 96,
    };

    private static BitmapSource? CopyPixelSourceToFrozenBitmap(IPixelSource ps)
    {
        int w = ps.Width;
        int h = ps.Height;
        if (w <= 0 || h <= 0)
            return null;

        if (!TryMapToWpfPixelFormat(ps.Format, out var wpfFmt))
            return null;

        int bpp = wpfFmt.BitsPerPixel;
        int stride = ((w * bpp + 31) / 32) * 4;
        var buffer = new byte[stride * h];
        var rect = new Rectangle(0, 0, w, h);
        ps.CopyPixels(rect, stride, buffer);

        var bs = BitmapSource.Create(w, h, 96, 96, wpfFmt, null, buffer, stride);
        if (bs.CanFreeze)
            bs.Freeze();
        return bs;
    }

    private static bool TryMapToWpfPixelFormat(object msFormat, out System.Windows.Media.PixelFormat wpf)
    {
        if (msFormat.Equals(MsPixelFormats.Bgra32bpp))
        {
            wpf = System.Windows.Media.PixelFormats.Bgra32;
            return true;
        }

        if (msFormat.Equals(MsPixelFormats.Bgr24bpp))
        {
            wpf = System.Windows.Media.PixelFormats.Bgr24;
            return true;
        }

        if (msFormat.Equals(MsPixelFormats.Grey8bpp))
        {
            wpf = System.Windows.Media.PixelFormats.Gray8;
            return true;
        }

        wpf = default;
        return false;
    }
}
