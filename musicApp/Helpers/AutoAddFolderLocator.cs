using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace musicApp.Helpers;

/// <summary>Finds iTunes' "Automatically Add to iTunes" drop folder and where its files should go.</summary>
public static class AutoAddFolderLocator
{
    private static readonly string[] DropFolderNames = { "Automatically Add to iTunes", "Automatically Add to Music" };

    /// <summary>First existing drop folder: next to a library "Music" folder, then iTunes' XML, then the default.</summary>
    public static string? Detect(IReadOnlyList<string> musicFolders)
    {
        var mediaFolders = musicFolders
            .Where(f => string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(f)), "Music",
                StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetDirectoryName)
            .Concat(MediaFoldersFromLibraryXml())
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Music", "iTunes", "iTunes Media"));

        foreach (var media in mediaFolders)
        {
            if (string.IsNullOrEmpty(media))
                continue;
            foreach (var name in DropFolderNames)
            {
                var candidate = Path.Combine(media, name);
                // Skip one inside a library folder (library root = all of Music): it could not be enabled.
                if (Directory.Exists(candidate) && DestinationRoot(candidate, musicFolders) != null)
                    return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// The organized "Music" folder next to the drop folder when it is in the library, else the first library
    /// folder. Null when there is no library folder, or when the drop folder overlaps one: importing would then
    /// move songs that are already in the library.
    /// </summary>
    public static string? DestinationRoot(string dropFolder, IReadOnlyList<string> musicFolders)
    {
        if (musicFolders.Any(f => LibraryPathHelper.IsFolderUnderOrEqual(dropFolder, f) ||
                                  LibraryPathHelper.IsFolderUnderOrEqual(f, dropFolder)))
            return null;
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(dropFolder)));
        if (parent != null)
        {
            var sibling = Path.Combine(parent, "Music");
            if (musicFolders.Any(f => LibraryPathHelper.IsFolderUnderOrEqual(sibling, f)))
                return sibling;
        }
        return musicFolders.Count > 0 ? musicFolders[0] : null;
    }

    // iTunes XMLs store "Music Folder" as a file:// URL of the media folder. Newest XML first; a stale one can
    // point at a drive that is gone, which the Directory.Exists check in Detect skips.
    private static IEnumerable<string> MediaFoldersFromLibraryXml()
    {
        var xmls = FruitAppLocalAlbumArtCache.BuildCandidateLibraryXmlPathsList()
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc);
        foreach (var xml in xmls)
        {
            var path = ReadMusicFolder(xml);
            if (path != null)
                yield return path;
        }
    }

    private static string? ReadMusicFolder(string xmlPath)
    {
        try
        {
            using var reader = XmlReader.Create(xmlPath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
            while (reader.ReadToFollowing("key"))
            {
                if (reader.ReadElementContentAsString() != "Music Folder")
                    continue;
                if (reader.MoveToContent() == XmlNodeType.Element && reader.Name == "string")
                    return FruitAppLocalAlbumArtCache.NormalizeFruitAppLocationToPath(reader.ReadElementContentAsString());
                return null;
            }
        }
        catch (Exception ex) when (ex is IOException or XmlException or UnauthorizedAccessException)
        {
        }
        return null;
    }
}
