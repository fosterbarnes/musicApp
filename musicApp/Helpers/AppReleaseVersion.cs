using System;
using System.IO;

namespace musicApp.Helpers;

internal static class AppReleaseVersion
{
    public static string ReadLabel()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Version");
            if (File.Exists(path))
            {
                var line = File.ReadAllText(path).Trim();
                if (!string.IsNullOrEmpty(line))
                    return line;
            }
        }
        catch
        {
            // ignored
        }

        return "0.0.0";
    }
}
