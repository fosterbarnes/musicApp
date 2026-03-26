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
        try
        {
            var path = Path.Combine(installRoot, "Version");
            if (!File.Exists(path))
                return null;
            var text = File.ReadAllText(path).Trim();
            if (!string.IsNullOrEmpty(text))
                return text.TrimStart('v', 'V');
        }
        catch
        {
            // ignore
        }

        return null;
    }

    public static string? TryReadVersionTag(string installRoot)
    {
        try
        {
            var path = Path.Combine(installRoot, "VersionTag");
            if (!File.Exists(path))
                return null;
            var text = File.ReadAllText(path).Trim();
            if (!string.IsNullOrEmpty(text))
                return text;
        }
        catch
        {
            // ignore
        }

        return null;
    }

    public static string? TryReadVersionBuild(string installRoot)
    {
        try
        {
            var path = Path.Combine(installRoot, "VersionBuild");
            if (!File.Exists(path))
                return null;
            return File.ReadAllText(path).Trim();
        }
        catch
        {
            return null;
        }
    }
}
