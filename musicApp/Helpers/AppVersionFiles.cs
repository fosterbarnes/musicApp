using System;
using System.IO;
using System.Reflection;

namespace musicApp.Helpers;

public static class AppVersionFiles
{
    public static string ReadLabel()
    {
        var ver = ReadVersionCore();
        return string.IsNullOrEmpty(ver) ? "0.0.0" : ver;
    }

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
        var raw = ReadVersionBuildCore();
        if (string.IsNullOrEmpty(raw))
            return "";

        return raw.Trim().ToLowerInvariant() switch
        {
            "portable" => " (portable)",
            "x64" => " (x64)",
            "x86" => " (x86)",
            "arm64" => " (arm64)",
            _ => ""
        };
    }

    private static string ReadVersionCore()
    {
        var fromFile = ReadVersionLine(0);
        if (!string.IsNullOrEmpty(fromFile))
            return fromFile.TrimStart('v', 'V');

        var av = Assembly.GetExecutingAssembly().GetName().Version;
        if (av == null)
            return "0.0.0";
        return av.Build >= 0 ? $"{av.Major}.{av.Minor}.{av.Build}" : $"{av.Major}.{av.Minor}";
    }

    private static string ReadVersionTagCore() => ReadVersionLine(1) ?? "";

    private static string? ReadVersionBuildCore() => ReadVersionLine(2);

    private static string? ReadVersionLine(int index)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Version");
            if (!File.Exists(path))
                return null;
            var lines = File.ReadAllLines(path);
            if (index < 0 || index >= lines.Length)
                return null;
            var text = lines[index].Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }
}
