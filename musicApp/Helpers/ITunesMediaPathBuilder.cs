using System;
using System.IO;
using System.Text;

namespace musicApp.Helpers;

/// <summary>
/// Builds the path iTunes uses when "Keep Media folder organized" is on:
/// <c>Artist\Album\[D-][NN ]Title.ext</c>, relative to <c>iTunes Media\Music</c>.
/// No WPF here: the test project compile-links this file.
/// </summary>
public static class ITunesMediaPathBuilder
{
    // Measured against an iTunes-organized library: folders cut at 40 characters, file names (with the
    // "1-01 " prefix, without the extension) at 36.
    private const int MaxFolderLength = 40;
    private const int MaxFileStemLength = 36;

    public static string BuildRelativePath(Song tags, string extension)
    {
        var artist = tags.IsCompilation ? "Compilations" : FirstNonBlank(tags.AlbumArtist, tags.Artist, "Unknown Artist");
        var album = FirstNonBlank(tags.Album, "Unknown Album");
        // No title tag (TrackMetadataLoader fills "Unknown Title"): iTunes keeps the original file name, unnumbered.
        var hasTitle = !string.IsNullOrWhiteSpace(tags.Title) && tags.Title != "Unknown Title";
        var name = hasTitle ? TrackPrefix(tags) + tags.Title.Trim() : Path.GetFileNameWithoutExtension(tags.FilePath);
        var stem = Clean(FirstNonBlank(name, "Unknown Title"), MaxFileStemLength, isFolder: false);
        return Path.Combine(
            Clean(artist, MaxFolderLength, isFolder: true),
            Clean(album, MaxFolderLength, isFolder: true),
            stem + extension);
    }

    /// <summary>
    /// Appends " 1", " 2"... before the extension until the path is free, like iTunes. A folder with that name
    /// counts as taken, or the move would fail on every retry.
    /// </summary>
    public static string ResolveCollision(string fullPath)
    {
        if (!Path.Exists(fullPath))
            return fullPath;
        var dir = Path.GetDirectoryName(fullPath) ?? "";
        var stem = Path.GetFileNameWithoutExtension(fullPath);
        var ext = Path.GetExtension(fullPath);
        for (var n = 1; ; n++)
        {
            var candidate = Path.Combine(dir, $"{stem} {n}{ext}");
            if (!Path.Exists(candidate))
                return candidate;
        }
    }

    // "01 " when the disc tag is missing or reads 1 of 1, "1-01 " for any other disc tag (iTunes adds the
    // prefix for "disc 1" with no disc count too), nothing without a track number.
    private static string TrackPrefix(Song tags)
    {
        if (tags.TrackNumber <= 0)
            return "";
        var track = tags.TrackNumber.ToString("00");
        var disc = ParseDisc(tags.DiscNumber, out var discCountFromTag);
        var discCount = Math.Max(tags.DiscTotal, discCountFromTag);
        return disc > 0 && !(disc == 1 && discCount == 1) ? $"{disc}-{track} " : $"{track} ";
    }

    // Disc tags come as "2" or "2/3".
    private static int ParseDisc(string? text, out int count)
    {
        count = 0;
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        var parts = text.Split('/');
        if (parts.Length > 1)
            int.TryParse(parts[1].Trim(), out count);
        return int.TryParse(parts[0].Trim(), out var disc) ? disc : 0;
    }

    private static string FirstNonBlank(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        return "";
    }

    // Illegal filename characters (plus ";" and "’", which iTunes also swaps) become "_", then cut to length.
    private static string Clean(string name, int maxLength, bool isFolder)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(c < 32 || "\\/:*?\"<>|;’".IndexOf(c) >= 0 ? '_' : c);
        var s = sb.ToString();
        if (s.Length > maxLength)
            s = s[..maxLength];
        s = s.TrimEnd(' ');
        // Windows drops a folder's trailing dot, so "Vol." would not round-trip. A file stem keeps it.
        if (isFolder && s.EndsWith('.'))
            s = s[..^1] + "_";
        if (s.StartsWith('.'))
            s = "_" + s[1..];
        if (s.Length == 0)
            return "_";
        // Windows reserves device names even with an extension ("NUL.mp3"); creating them fails on every retry.
        return IsReservedDeviceName(s.Split('.')[0].TrimEnd()) ? "_" + s : s;
    }

    private static bool IsReservedDeviceName(string name) =>
        name.ToUpperInvariant() switch
        {
            "CON" or "PRN" or "AUX" or "NUL" => true,
            { Length: 4 } n when (n.StartsWith("COM") || n.StartsWith("LPT")) &&
                                 n[3] is (>= '1' and <= '9') or '¹' or '²' or '³' => true,
            _ => false
        };
}
