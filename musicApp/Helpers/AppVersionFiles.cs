using System;
using System.IO;
using System.Reflection;

namespace MusicApp.Helpers;

public static class AppVersionFiles
{
    public static string GetAboutVersionSubtitle()
    {
        var ver = ReadVersionCore();
        var tag = ReadVersionTagCore();
        if (!string.IsNullOrEmpty(tag))
            return $"v{ver} ({tag})";
        return $"v{ver}";
    }

    private static string ReadVersionCore()
    {
        var fromFile = ReadTrimmedFile("Version");
        if (!string.IsNullOrEmpty(fromFile))
            return fromFile.TrimStart('v', 'V');

        var av = Assembly.GetExecutingAssembly().GetName().Version;
        if (av == null)
            return "0.0.0";
        return av.Build >= 0 ? $"{av.Major}.{av.Minor}.{av.Build}" : $"{av.Major}.{av.Minor}";
    }

    private static string ReadVersionTagCore() => ReadTrimmedFile("VersionTag") ?? "";

    private static string? ReadTrimmedFile(string fileName)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, fileName);
            if (File.Exists(path))
                return File.ReadAllText(path).Trim();
        }
        catch
        {
            // ignore
        }

        return null;
    }
}
