using System;
using System.Drawing;
using System.IO;
using ATL;

namespace musicApp.Helpers;

/// <summary>Strong embedded front cover detection; used to skip system/cache artwork apply.</summary>
public static class EmbeddedCoverEligibility
{
    private const int MinPictureBytes = 3072;
    private const int MinRasterDimensionPx = 120;

    public static bool HasStrongEmbeddedFrontCover(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        try
        {
            var t = new Track(filePath);
            return HasStrongEmbeddedFrontCover(t);
        }
        catch
        {
            return false;
        }
    }

    public static bool ShouldSkipSystemCacheEmbed(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return true;

        try
        {
            var t = new Track(filePath);
            return HasStrongEmbeddedFrontCover(t);
        }
        catch
        {
            return true;
        }
    }

    private static bool HasStrongEmbeddedFrontCover(Track track)
    {
        var pics = track.EmbeddedPictures;
        if (pics == null || pics.Count == 0)
            return false;

        var data = pics[0].PictureData;
        if (data == null || data.Length < MinPictureBytes)
            return false;

        try
        {
            using var ms = new MemoryStream(data, 0, data.Length, writable: false, publiclyVisible: true);
            using var img = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false);
            return img.Width >= MinRasterDimensionPx && img.Height >= MinRasterDimensionPx;
        }
        catch
        {
            return false;
        }
    }
}
