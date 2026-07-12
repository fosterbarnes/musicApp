using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using musicApp.Helpers;

namespace musicApp;

/// <summary>
/// Single-file backup of <c>settings.json</c> + <c>preferences.json</c> (not the music library).
/// </summary>
public static class SettingsBundleIO
{
    public const string FormatId = "musicApp.settingsBundle";
    public const int CurrentFormatVersion = 1;
    public const string FileFilter = "JSON (*.json)|*.json|All files (*.*)|*.*";
    public const string DefaultExportFileName = "musicApp-settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public sealed class Bundle
    {
        public string Format { get; set; } = FormatId;
        public int FormatVersion { get; set; } = CurrentFormatVersion;
        public string? ExportedAtUtc { get; set; }
        public string? AppVersion { get; set; }
        public SettingsManager.AppSettings? Settings { get; set; }
        public PreferencesManager.AppPreferences? Preferences { get; set; }
    }

    public static void Export(
        string path,
        SettingsManager.AppSettings settings,
        PreferencesManager.AppPreferences preferences)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(preferences);

        PreferencesManager.EnsureInitialized(preferences);

        var bundle = new Bundle
        {
            Format = FormatId,
            FormatVersion = CurrentFormatVersion,
            ExportedAtUtc = DateTime.UtcNow.ToString("o"),
            AppVersion = AppVersionFiles.ReadLabel(),
            Settings = settings,
            Preferences = preferences
        };

        var json = JsonSerializer.Serialize(bundle, JsonOptions);
        File.WriteAllText(path, json);
    }

    public static Bundle Import(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException("Settings file not found.", path);

        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("Settings file is empty.");

        Bundle? bundle;
        try
        {
            bundle = JsonSerializer.Deserialize<Bundle>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Settings file is not valid JSON.", ex);
        }

        if (bundle == null)
            throw new InvalidDataException("Settings file could not be read.");

        if (!string.Equals(bundle.Format, FormatId, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Unrecognized settings format (expected \"{FormatId}\").");

        if (bundle.FormatVersion < 1 || bundle.FormatVersion > CurrentFormatVersion)
            throw new InvalidDataException(
                $"Unsupported settings format version {bundle.FormatVersion} (supported: 1–{CurrentFormatVersion}).");

        if (bundle.Settings == null && bundle.Preferences == null)
            throw new InvalidDataException("Settings file has no settings or preferences payload.");

        if (bundle.Preferences != null)
            PreferencesManager.EnsureInitialized(bundle.Preferences);

        if (bundle.Settings != null)
        {
            bundle.Settings.Player ??= new SettingsManager.PlayerSettings();
            bundle.Settings.WindowState ??= new SettingsManager.WindowStateSettings();
        }

        return bundle;
    }
}
