using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

namespace musicApp.Helpers;

/// <summary>
/// Maps audio files to local Album Artwork cache (.itc/.itc2) using the vendor library plist/XML layout
/// under <c>Apple Computer\iTunes\</c> (on-disk paths unchanged).
/// </summary>
public static class FruitAppLocalAlbumArtCache
{
    private static readonly object Gate = new();
    private static Dictionary<string, string>? _audioPathToItc;
    private static bool _indexLoaded;

    public static int LastIndexedPathCount { get; private set; }

    public static string? LastCacheRootUsed { get; private set; }

    public static IReadOnlyList<string> LastLibraryXmlPathsConsidered { get; private set; } =
        Array.Empty<string>();

    public static void ReloadIndex()
    {
        lock (Gate)
        {
            _indexLoaded = false;
            _audioPathToItc = null;
        }
    }

    public static void EnsureIndexLoaded()
    {
        lock (Gate)
        {
            if (_indexLoaded)
                return;
            _audioPathToItc = TryBuildPathIndex() ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _indexLoaded = true;
        }
    }

    public static bool TryGetItcCacheFileForAudioPath(string audioFilePath, out string? itcPath)
    {
        EnsureIndexLoaded();
        itcPath = null;
        var key = LibraryPathHelper.TryNormalizePath(audioFilePath);
        if (string.IsNullOrEmpty(key))
            return false;
        if (_audioPathToItc!.TryGetValue(key, out itcPath))
            return true;
        if (key.StartsWith(@"\\?\", StringComparison.Ordinal) && key.Length > 4)
            return _audioPathToItc.TryGetValue(key[4..], out itcPath);
        if (OperatingSystem.IsWindows() && key.Length >= 2 && char.IsLetter(key[0]) && key[1] == ':')
            return _audioPathToItc.TryGetValue(@"\\?\" + key, out itcPath);
        return false;
    }

    /// <summary>Decoded raster bytes suitable for embedding (JPEG/PNG as stored).</summary>
    public static byte[]? TryGetCoverImageBytesForAudioPath(string audioFilePath)
    {
        if (!TryGetItcCacheFileForAudioPath(audioFilePath, out var itc) || string.IsNullOrEmpty(itc))
            return null;
        return TryExtractCoverImageBytesFromItcFile(itc);
    }

    public static Bitmap? TryLoadBitmapForAudioFile(string audioFilePath)
    {
        var bytes = TryGetCoverImageBytesForAudioPath(audioFilePath);
        if (bytes == null || bytes.Length < 24)
            return null;
        try
        {
            using var ms = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true);
            using var img = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false);
            return new Bitmap(img);
        }
        catch
        {
            return null;
        }
    }

    public static byte[]? TryExtractCoverImageBytesFromItcFile(string itcFilePath)
    {
        byte[] data;
        try
        {
            data = File.ReadAllBytes(itcFilePath);
        }
        catch
        {
            return null;
        }

        if (TryExtractJpegOrPngFromBlob(data, out var raster))
            return raster;

        using var bmp = TryExtractBgraBitmapFromItc(data);
        if (bmp == null)
            return null;
        try
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Jpeg);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, string>? TryBuildPathIndex()
    {
        var cacheRoot = FindExistingCacheRoot();
        LastCacheRootUsed = cacheRoot;
        var xmlCandidates = BuildCandidateLibraryXmlPathsList();
        LastLibraryXmlPathsConsidered = xmlCandidates;
        if (string.IsNullOrEmpty(cacheRoot))
        {
            LastIndexedPathCount = 0;
            Debug.WriteLine("[FruitAppLocalAlbumArtCache] NoAlbumArtworkCacheFolder");
            return null;
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var xmlPath in xmlCandidates)
        {
            if (!File.Exists(xmlPath))
                continue;
            try
            {
                var doc = XDocument.Load(xmlPath, LoadOptions.PreserveWhitespace);
                var plist = doc.Root;
                if (plist == null || !string.Equals(plist.Name.LocalName, "plist", StringComparison.Ordinal))
                    continue;
                var mainDict = plist.Elements().FirstOrDefault(e => e.Name.LocalName == "dict");
                if (mainDict == null)
                    continue;

                string? libId = null;
                string? musicFolderNorm = null;
                XElement? tracksDict = null;
                foreach (var (k, v) in EnumerateDictPairs(mainDict))
                {
                    if (k == "Library Persistent ID" && string.Equals(v.Name.LocalName, "string", StringComparison.Ordinal))
                        libId = v.Value?.Trim();
                    if (k == "Music Folder" && string.Equals(v.Name.LocalName, "string", StringComparison.Ordinal))
                        musicFolderNorm = NormalizeFruitAppLocationToPath(v.Value ?? "");
                    if (k == "Tracks" && string.Equals(v.Name.LocalName, "dict", StringComparison.Ordinal))
                        tracksDict = v;
                }

                if (string.IsNullOrEmpty(libId) || tracksDict == null)
                    continue;

                foreach (var (_, trackEl) in EnumerateDictPairs(tracksDict))
                {
                    if (!string.Equals(trackEl.Name.LocalName, "dict", StringComparison.Ordinal))
                        continue;

                    string? pid = null;
                    string? loc = null;
                    foreach (var (fk, fv) in EnumerateDictPairs(trackEl))
                    {
                        if (fk == "Persistent ID" && string.Equals(fv.Name.LocalName, "string", StringComparison.Ordinal))
                            pid = fv.Value?.Trim();
                        if (fk == "Location" && string.Equals(fv.Name.LocalName, "string", StringComparison.Ordinal))
                            loc = fv.Value;
                    }

                    if (string.IsNullOrEmpty(pid) || string.IsNullOrEmpty(loc))
                        continue;

                    if (!TryBuildItcPath(cacheRoot, libId, pid, out var itcPath))
                        continue;

                    var resolved = ResolveAudioPathFromFruitAppLocation(loc, musicFolderNorm);
                    var key = LibraryPathHelper.TryNormalizePath(resolved);
                    if (string.IsNullOrEmpty(key))
                        continue;
                    map.TryAdd(key, itcPath);
                    if (OperatingSystem.IsWindows() && key.Length >= 2 && char.IsLetter(key[0]) && key[1] == ':')
                        map.TryAdd(@"\\?\" + key, itcPath);
                }
            }
            catch
            {
                // ignore unreadable XML
            }
        }

        LastIndexedPathCount = map.Count;
        Debug.WriteLine(
            $"[FruitAppLocalAlbumArtCache] cacheRoot={cacheRoot}; mapEntries={map.Count}; xmlListCount={xmlCandidates.Count}");
        return map;
    }

    private static List<string> BuildCandidateLibraryXmlPathsList()
    {
        var list = new List<string>(12);
        foreach (var p in EnumerateLibraryXmlPathsFromFruitAppPrefs())
        {
            if (!string.IsNullOrWhiteSpace(p))
                list.Add(p);
        }

        try
        {
            var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (!string.IsNullOrEmpty(music))
                list.Add(Path.Combine(music, "iTunes", "iTunes Library.xml"));
        }
        catch { }

        try
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(profile))
                list.Add(Path.Combine(profile, "Music", "iTunes", "iTunes Library.xml"));
        }
        catch { }

        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(local))
                list.Add(Path.Combine(local, "Apple Computer", "iTunes", "iTunes Library.xml"));
        }
        catch { }

        try
        {
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(roaming))
                list.Add(Path.Combine(roaming, "Apple Computer", "iTunes", "iTunes Library.xml"));
        }
        catch { }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> EnumerateLibraryXmlPathsFromFruitAppPrefs()
    {
        string[] prefsPaths =
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Apple Computer", "iTunes", "iTunesPrefs.xml"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Apple Computer", "iTunes", "iTunesPrefs.xml")
        };

        foreach (var prefsPath in prefsPaths)
        {
            if (string.IsNullOrEmpty(prefsPath) || !File.Exists(prefsPath))
                continue;
            foreach (var p in TryParseLibraryXmlHintsFromPrefsFile(prefsPath))
                yield return p;
        }
    }

    private static List<string> TryParseLibraryXmlHintsFromPrefsFile(string prefsPath)
    {
        var result = new List<string>();
        try
        {
            var doc = XDocument.Load(prefsPath, LoadOptions.PreserveWhitespace);
            var root = doc.Root;
            if (root == null)
                return result;

            foreach (var el in root.Descendants())
            {
                if (string.Equals(el.Name.LocalName, "string", StringComparison.Ordinal))
                {
                    var norm = NormalizePossibleLibraryXmlPath(el.Value);
                    if (!string.IsNullOrEmpty(norm))
                        result.Add(norm);
                }
                else if (string.Equals(el.Name.LocalName, "data", StringComparison.Ordinal))
                {
                    var raw = (el.Value ?? "").Trim().Replace("\n", "").Replace("\r", "");
                    if (raw.Length < 4)
                        continue;
                    try
                    {
                        var bytes = Convert.FromBase64String(raw);
                        var norm = NormalizePossibleLibraryXmlPath(DecodePrefsBytesToPathString(bytes));
                        if (!string.IsNullOrEmpty(norm))
                            result.Add(norm);
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch
        {
        }

        return result;
    }

    private static string? DecodePrefsBytesToPathString(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return null;
        try
        {
            if (bytes.Length >= 4 && bytes[1] == 0 && bytes[3] == 0)
                return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
            return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizePossibleLibraryXmlPath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var t = raw.Trim();
        if (t.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            var u = NormalizeFruitAppLocationToPath(t);
            if (!string.IsNullOrEmpty(u) && u.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                return u;
            return null;
        }

        if (!t.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            return null;
        if (t.IndexOf("iTunes", StringComparison.OrdinalIgnoreCase) < 0)
            return null;
        if (!t.Contains(':', StringComparison.Ordinal) && !t.StartsWith("\\\\", StringComparison.Ordinal))
            return null;
        return LibraryPathHelper.TryNormalizePath(t);
    }

    private static string? ResolveAudioPathFromFruitAppLocation(string location, string? musicFolderNorm)
    {
        var t = location.Trim();
        if (string.IsNullOrEmpty(t))
            return null;

        if (t.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return NormalizeFruitAppLocationToPath(t);

        if (string.IsNullOrEmpty(musicFolderNorm))
            return null;

        try
        {
            var rel = Uri.UnescapeDataString(t.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            var combined = Path.Combine(
                musicFolderNorm.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                rel);
            return LibraryPathHelper.TryNormalizePath(combined);
        }
        catch
        {
            return null;
        }
    }

    private static string? FindExistingCacheRoot()
    {
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(local))
            {
                var classic = Path.Combine(local, "Apple Computer", "iTunes", "Album Artwork", "Cache");
                if (Directory.Exists(classic))
                    return classic;

                var appleMusic = Path.Combine(local, "Apple Music", "Album Artwork", "Cache");
                if (Directory.Exists(appleMusic))
                    return appleMusic;
            }
        }
        catch { }

        return null;
    }

    private static IEnumerable<(string key, XElement value)> EnumerateDictPairs(XElement dict)
    {
        XElement? pendingKey = null;
        foreach (var child in dict.Elements())
        {
            if (string.Equals(child.Name.LocalName, "key", StringComparison.Ordinal))
                pendingKey = child;
            else if (pendingKey != null)
            {
                yield return (pendingKey.Value ?? "", child);
                pendingKey = null;
            }
        }
    }

    internal static string? NormalizeFruitAppLocationToPath(string location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return null;

        var s = location.Trim();
        try
        {
            if (s.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(s);
                return LibraryPathHelper.TryNormalizePath(uri.LocalPath);
            }
        }
        catch
        {
            // manual fallback
        }

        if (!s.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            return null;

        var rest = s.AsSpan("file://".Length);
        if (rest.StartsWith("localhost".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            rest = rest["localhost".Length..];
            if (rest.Length > 0 && (rest[0] == '/' || rest[0] == '\\'))
                rest = rest[1..];
        }
        else if (rest.StartsWith("127.0.0.1".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            rest = rest["127.0.0.1".Length..];
            if (rest.Length > 0 && (rest[0] == '/' || rest[0] == '\\'))
                rest = rest[1..];
        }
        else if (rest.Length > 0 && rest[0] == '/')
            rest = rest[1..];

        var built = rest.ToString()
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('|', ':');
        if (built.Length >= 3 && built[0] == '/' && char.IsLetter(built[1]) &&
            (built[2] == ':' || built[2] == Path.VolumeSeparatorChar))
            built = built.TrimStart('/');

        try
        {
            return LibraryPathHelper.TryNormalizePath(Uri.UnescapeDataString(built));
        }
        catch
        {
            return null;
        }
    }

    private static bool TryBuildItcPath(string cacheRoot, string libraryId, string trackPersistentId, out string itcPath)
    {
        itcPath = "";
        if (string.IsNullOrEmpty(trackPersistentId) || trackPersistentId.Length < 3)
            return false;

        int n = trackPersistentId.Length;
        if (HexNybble(trackPersistentId[n - 1]) < 0 || HexNybble(trackPersistentId[n - 2]) < 0 ||
            HexNybble(trackPersistentId[n - 3]) < 0)
            return false;

        int d1 = HexNybble(trackPersistentId[n - 1]);
        int d2 = HexNybble(trackPersistentId[n - 2]);
        int d3 = HexNybble(trackPersistentId[n - 3]);
        var baseName = $"{libraryId}-{trackPersistentId}";
        var sub = Path.Combine(
            libraryId,
            d1.ToString("D2", System.Globalization.CultureInfo.InvariantCulture),
            d2.ToString("D2", System.Globalization.CultureInfo.InvariantCulture),
            d3.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));

        foreach (var ext in new[] { ".itc2", ".itc" })
        {
            var p = Path.Combine(cacheRoot, sub, baseName + ext);
            if (File.Exists(p))
            {
                itcPath = p;
                return true;
            }
        }

        return false;
    }

    private static int HexNybble(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        return -1;
    }

    private static bool TryExtractJpegOrPngFromBlob(ReadOnlySpan<byte> data, out byte[] imageBytes)
    {
        imageBytes = Array.Empty<byte>();
        int bestStart = -1;
        int bestLen = 0;

        for (int i = 0; i < data.Length - 3; i++)
        {
            if (data[i] != 0xFF || data[i + 1] != 0xD8 || data[i + 2] != 0xFF)
                continue;

            for (int j = i + 2; j < data.Length - 1; j++)
            {
                if (data[j] != 0xFF || data[j + 1] != 0xD9)
                    continue;
                int len = j - i + 2;
                if (len > bestLen)
                {
                    bestLen = len;
                    bestStart = i;
                }
                break;
            }
        }

        if (bestStart >= 0 && bestLen > 64)
        {
            imageBytes = data.Slice(bestStart, bestLen).ToArray();
            return true;
        }

        ReadOnlySpan<byte> pngSig = [137, 80, 78, 71, 13, 10, 26, 10];
        int p = data.IndexOf(pngSig);
        if (p >= 0 && TryFindPngIendEnd(data, p, out int endInclusive) && endInclusive > p)
        {
            imageBytes = data.Slice(p, endInclusive - p + 1).ToArray();
            return imageBytes.Length > 32;
        }

        return false;
    }

    private static bool TryFindPngIendEnd(ReadOnlySpan<byte> data, int pngStart, out int endInclusive)
    {
        endInclusive = 0;
        ReadOnlySpan<byte> iend = "IEND"u8;
        for (int i = pngStart + 8; i < data.Length - 8; i++)
        {
            if (data[i] != iend[0]) continue;
            if (!data.Slice(i, 4).SequenceEqual(iend)) continue;
            endInclusive = i + 4 + 4 - 1;
            return endInclusive < data.Length;
        }
        return false;
    }

    private static Bitmap? TryExtractBgraBitmapFromItc(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8) return null;
        int off = (int)ReadBEUInt32(data);
        if (off <= 0 || off >= data.Length) return null;

        int cursor = off;
        while (cursor + 196 <= data.Length)
        {
            uint s = ReadBEUInt32(data.Slice(cursor));
            cursor += 4;
            if (s < 200 || s > int.MaxValue) break;

            if (cursor + 188 > data.Length) break;
            var item = data.Slice(cursor, 188);
            ReadOnlySpan<byte> itemTag = "item"u8;
            if (!item.Slice(0, 4).SequenceEqual(itemTag)) break;

            var fmt = Encoding.ASCII.GetString(item.Slice(44, 4));
            uint w = ReadBEUInt32(item.Slice(52));
            uint h = ReadBEUInt32(item.Slice(56));
            cursor += 188;

            if (cursor + 4 > data.Length) break;
            ReadOnlySpan<byte> dataTag = "data"u8;
            if (!data.Slice(cursor, 4).SequenceEqual(dataTag))
                break;
            cursor += 4;

            if (fmt == "bGRA" && w > 0 && h > 0 && w < 8000 && h < 8000)
            {
                int need = (int)(w * h * 4);
                if (cursor + need <= data.Length)
                    return BgraSpanToBitmap(data.Slice(cursor, need), (int)w, (int)h);
                return null;
            }

            int skip = (int)s - 196 - 4;
            if (skip < 0 || cursor + skip > data.Length) break;
            cursor += skip;
        }

        return null;
    }

    private static Bitmap BgraSpanToBitmap(ReadOnlySpan<byte> bgra, int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = bd.Stride;
            var row = new byte[w * 4];
            for (int y = 0; y < h; y++)
            {
                bgra.Slice(y * w * 4, w * 4).CopyTo(row);
                Marshal.Copy(row, 0, IntPtr.Add(bd.Scan0, y * stride), w * 4);
            }
        }
        finally
        {
            bmp.UnlockBits(bd);
        }

        return bmp;
    }

    private static uint ReadBEUInt32(ReadOnlySpan<byte> b)
    {
        if (b.Length < 4) return 0;
        return (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
    }
}
