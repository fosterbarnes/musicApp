using System;
using System.IO;

namespace musicApp.Helpers;

public static class AppPaths
{
    // MUSICAPP_DATA_DIR keeps test runs away from the real library and settings.
    public static string AppDataDirectory { get; } =
        Environment.GetEnvironmentVariable("MUSICAPP_DATA_DIR") is { Length: > 0 } dataDir
            ? dataDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "musicApp");

    public static string PreferencesPath => Path.Combine(AppDataDirectory, "preferences.json");
    public static string SettingsPath => Path.Combine(AppDataDirectory, "settings.json");

    public static void EnsureAppDataDirectory() => Directory.CreateDirectory(AppDataDirectory);
}
