using System;
using System.IO;
using System.Linq;
using ATL;

namespace musicApp.Helpers;

public static class AlbumArtSourceResolver
{
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };

    public static byte[]? TryLoadEmbeddedBytes(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var atlTrack = new Track(fs);
            var embeddedPictures = atlTrack.EmbeddedPictures;
            if (embeddedPictures != null && embeddedPictures.Count > 0)
                return embeddedPictures[0].PictureData;
        }
        catch
        {
        }

        return null;
    }

    public static string? TryFindDirectoryCoverPath(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (directory == null || !Directory.Exists(directory))
            return null;

        var imageFiles = Directory.GetFiles(directory, "*.*")
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();

        return imageFiles.FirstOrDefault(file =>
        {
            var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            return name.Contains("album") || name.Contains("cover") || name.Contains("art") || name.Contains("folder");
        }) ?? imageFiles.FirstOrDefault();
    }

    public static byte[]? TryLoadItunesCacheBytes(string filePath) =>
        FruitAppLocalAlbumArtCache.TryGetCoverImageBytesForAudioPath(filePath);
}
