using System.Diagnostics.CodeAnalysis;

namespace musicApp.Updater;

internal static class VersionComparer
{
    public static bool TryParse(string raw, [NotNullWhen(true)] out Version? version)
    {
        var s = raw.Trim().TrimStart('v', 'V');
        if (string.IsNullOrEmpty(s))
        {
            version = null;
            return false;
        }

        return Version.TryParse(s, out version);
    }
}
