namespace musicApp.Updater;

internal enum VersionBuild
{
    Portable,
    X64Installer,
    X86Installer,
}

internal static class VersionBuildExtensions
{
    public static bool TryParseFromFileContent(string? raw, out VersionBuild kind)
    {
        kind = VersionBuild.Portable;
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        switch (raw.Trim().ToLowerInvariant())
        {
            case "portable":
                kind = VersionBuild.Portable;
                return true;
            case "x64":
                kind = VersionBuild.X64Installer;
                return true;
            case "x86":
                kind = VersionBuild.X86Installer;
                return true;
            default:
                return false;
        }
    }
}
