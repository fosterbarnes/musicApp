using System;
using System.IO;

namespace musicApp.Helpers;

public static class AppPaths
{
    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "musicApp");

    public static string PreferencesPath => Path.Combine(AppDataDirectory, "preferences.json");
    public static string SettingsPath => Path.Combine(AppDataDirectory, "settings.json");

    public static void EnsureAppDataDirectory() => Directory.CreateDirectory(AppDataDirectory);
}
