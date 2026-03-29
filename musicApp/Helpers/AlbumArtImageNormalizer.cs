using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace musicApp.Helpers;

public static class AlbumArtImageNormalizer
{
    private const int MaxDimensionPx = 8192;
    private const int MinDimensionPx = 16;
    private const double SquareToleranceRatio = 0.01;

    private static readonly object JpegEncodeLock = new();

    public static bool TryNormalizeForEmbed(byte[] input, out byte[] output)
    {
        output = Array.Empty<byte>();
        if (input is not { Length: > 24 })
            return false;

        if (!LooksLikeRasterHeader(input))
            return false;

        Bitmap bmp;
        try
        {
            using var ms = new MemoryStream(input, 0, input.Length, writable: false, publiclyVisible: true);
            using var img = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: true);

            int w = img.Width;
            int h = img.Height;
            if (w < MinDimensionPx || h < MinDimensionPx || w > MaxDimensionPx || h > MaxDimensionPx)
                return false;

            if (IsSquareEnough(w, h))
            {
                output = input;
                return true;
            }

            bmp = CenterCropToSquare(img, w, h);
        }
        catch
        {
            return false;
        }

        try
        {
            using (bmp)
            {
                output = EncodeAsJpeg(bmp, 92L);
            }
            return output.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSquareEnough(int w, int h)
    {
        if (w == h) return true;
        double diff = Math.Abs(w - h) / (double)Math.Max(w, h);
        return diff <= SquareToleranceRatio;
    }

    private static Bitmap CenterCropToSquare(Image src, int w, int h)
    {
        int side = Math.Min(w, h);
        int srcX = (w - side) / 2;
        int srcY = (h - side) / 2;

        var cropped = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(cropped))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.DrawImage(src,
                new Rectangle(0, 0, side, side),
                new Rectangle(srcX, srcY, side, side),
                GraphicsUnit.Pixel);
        }

        return cropped;
    }

    private static byte[] EncodeAsJpeg(Bitmap bmp, long quality)
    {
        lock (JpegEncodeLock)
        {
            using var ms = new MemoryStream();
            var jpegEncoder = ImageCodecInfo.GetImageEncoders()
                .FirstOrDefault(e => e.FormatID == ImageFormat.Jpeg.Guid);

            if (jpegEncoder != null)
            {
                using var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                bmp.Save(ms, jpegEncoder, encoderParams);
            }
            else
                bmp.Save(ms, ImageFormat.Jpeg);

            return ms.ToArray();
        }
    }

    private static bool LooksLikeRasterHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4) return false;
        if (data[0] == 0xFF && data[1] == 0xD8) return true;
        if (data[0] == 0x89 && data[1] == 'P' && data[2] == 'N' && data[3] == 'G') return true;
        if (data[0] == 'G' && data[1] == 'I' && data[2] == 'F') return true;
        if (data[0] == 'B' && data[1] == 'M') return true;
        return false;
    }
}
