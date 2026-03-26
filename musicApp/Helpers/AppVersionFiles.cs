using System;
using System.IO;
using System.Reflection;

namespace musicApp.Helpers;

public static class AppVersionFiles
{
    public static string GetGitHubReleaseUrlForCurrentVersion()
    {
        var ver = ReadVersionCore().Trim().TrimStart('v', 'V');
        if (string.IsNullOrEmpty(ver))
            ver = "0.0.0";
        return $"https://github.com/fosterbarnes/musicApp/releases/tag/v{ver}";
    }

    public static string GetAboutVersionSubtitle()
    {
        var ver = ReadVersionCore();
        var tag = ReadVersionTagCore();
        if (!string.IsNullOrEmpty(tag))
            return $"v{ver} {tag}";
        return $"v{ver}";
    }

    /// <summary>Suffix for the About title, e.g. <c> (portable)</c>, or empty if missing/unknown.</summary>
    public static string GetAboutTitleSuffix()
    {
        var raw = ReadTrimmedFile("VersionBuild");
        if (string.IsNullOrEmpty(raw))
            return "";

        return raw.Trim().ToLowerInvariant() switch
        {
            "portable" => " (portable)",
            "x64" => " (x64)",
            "x86" => " (x86)",
            _ => ""
        };
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
