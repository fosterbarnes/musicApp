using System.IO;

namespace musicApp.Updater;

internal static class InstallVersionReader
{
    public static string? TryResolveInstallRoot(string? installRootOverride)
    {
        if (!string.IsNullOrWhiteSpace(installRootOverride))
        {
            try
            {
                var full = Path.GetFullPath(installRootOverride.Trim());
                if (!File.Exists(Path.Combine(full, "Version")))
                    return null;
                return full;
            }
            catch
            {
                return null;
            }
        }

        try
        {
            var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (File.Exists(Path.Combine(baseDir, "Version")))
                return baseDir;
            var parent = Directory.GetParent(baseDir)?.FullName;
            if (!string.IsNullOrEmpty(parent) && File.Exists(Path.Combine(parent, "Version")))
                return parent;
        }
        catch
        {
            // ignore
        }

        return null;
    }

    public static string? TryReadVersion(string installRoot)
    {
        var text = ReadVersionLine(installRoot, 0);
        if (string.IsNullOrEmpty(text))
            return null;
        return text.TrimStart('v', 'V');
    }

    public static string? TryReadVersionTag(string installRoot) => ReadVersionLine(installRoot, 1);

    public static string? TryReadVersionBuild(string installRoot) => ReadVersionLine(installRoot, 2);

    private static string? ReadVersionLine(string installRoot, int index)
    {
        try
        {
            var path = Path.Combine(installRoot, "Version");
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
