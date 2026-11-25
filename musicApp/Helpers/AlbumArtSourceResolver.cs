using System;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using ATL;

namespace musicApp.Helpers;

public static class AlbumArtSourceResolver
{
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
    private static readonly ConcurrentDictionary<string, string> DirectoryCoverCache = new(StringComparer.OrdinalIgnoreCase);

    public static byte[]? TryLoadEmbeddedBytes(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var atlTrack = new Track(fs);
            var embeddedPictures = atlTrack.EmbeddedPictures;
            // Files can carry a back cover or artist photo before the front cover.
            if (embeddedPictures != null && embeddedPictures.Count > 0)
                return (embeddedPictures.FirstOrDefault(p => p.PicType == PictureInfo.PIC_TYPE.Front)
                        ?? embeddedPictures[0]).PictureData;
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

        if (DirectoryCoverCache.TryGetValue(directory, out var cached))
            return string.IsNullOrEmpty(cached) ? null : cached;

        var imageFiles = Directory.GetFiles(directory, "*.*")
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();

        var result = imageFiles.FirstOrDefault(file =>
        {
            var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            // "art" only as the whole name: as a substring it also matched artist.jpg, party.jpg, heart.png.
            return name.Contains("album") || name.Contains("cover") || name.Contains("artwork") ||
                   name.Contains("folder") || name == "art";
        }) ?? imageFiles.FirstOrDefault();
        DirectoryCoverCache[directory] = result ?? "";
        return result;
    }

    public static void ClearDirectoryCoverCache() => DirectoryCoverCache.Clear();

    public static byte[]? TryLoadItunesCacheBytes(string filePath) =>
        FruitAppLocalAlbumArtCache.TryGetCoverImageBytesForAudioPath(filePath);
}
