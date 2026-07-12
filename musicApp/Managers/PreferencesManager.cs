using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using musicApp.Helpers;

namespace musicApp
{
    /// <summary><c>preferences.json</c> in %AppData%/musicApp (included when Clear Settings recycles *.json there).</summary>
    public class PreferencesManager
    {
        private static readonly string PreferencesFilePath = AppPaths.PreferencesPath;

        private static readonly JsonSerializerOptions LoadOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        private static readonly JsonSerializerOptions SaveOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        public class AppPreferences
        {
            public GeneralPreferences General { get; set; } = new GeneralPreferences();
            public SidebarPreferences Sidebar { get; set; } = new SidebarPreferences();
            public PlaybackPreferences Playback { get; set; } = new PlaybackPreferences();
            public LibraryPreferences Library { get; set; } = new LibraryPreferences();
            public ThemePreferences Theme { get; set; } = new ThemePreferences();
        }

        public class ThemePreferences
        {
            /// <summary>When true, donation links are shown in About (and any other donation UI).</summary>
            public bool ShowDonationLinks { get; set; } = true;
        }

        public class LibraryPreferences
        {
        }

        public class PlaybackPreferences
        {
            public bool VolumeNormalization { get; set; }

            public int CrossfadeSeconds { get; set; }

            public double CrossfadeRampSeconds { get; set; } = 2d;

            public AudioOutputBackend AudioBackend { get; set; } = AudioOutputBackend.WasapiShared;

            /// <summary>True = mixer chain volume; false = device volume; Settings checkbox label is inverted.</summary>
            public bool UseSoftwareSessionVolume { get; set; } = true;

            public int OutputSampleRateHz { get; set; } = PlaybackResampler.DefaultOutputSampleRateHz;

            public PlaybackOutputBits OutputBits { get; set; } = PlaybackOutputBitsUtil.Default;
        }

        public class GeneralPreferences
        {
            public const double UiFontSizeMin = 10;
            public const double UiFontSizeMax = 22;
            public const double UiFontSizeDefault = 14;

            public bool CheckForUpdates { get; set; }

            public bool AutomaticallyInstallUpdates { get; set; }

            public bool LaunchAppAfterUpdate { get; set; }

            public string Language { get; set; } = "en-system";

            /// <summary>WPF font family source string; empty = system / message font.</summary>
            public string UiFontFamily { get; set; } = "";

            public double UiFontSize { get; set; } = UiFontSizeDefault;
        }

        public class SidebarPreferences
        {
            public bool ShowAddMusic { get; set; }
            public bool ShowRescanLibrary { get; set; }
            public bool ShowRemoveMusic { get; set; }
            public bool ShowClearSettings { get; set; }
        }

        private static PreferencesManager? _instance;
        private static readonly object _lock = new object();

        public static PreferencesManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new PreferencesManager();
                    }
                }
                return _instance;
            }
        }

        private PreferencesManager()
        {
            AppPaths.EnsureAppDataDirectory();
        }

        public AppPreferences LoadPreferencesSync()
        {
            try
            {
                if (File.Exists(PreferencesFilePath))
                {
                    var json = File.ReadAllText(PreferencesFilePath);
                    var prefs = JsonSerializer.Deserialize<AppPreferences>(json, LoadOptions);
                    if (prefs != null)
                    {
                        EnsureInitialized(prefs);
                        ApplyLegacyUpdatePreferenceMigrations(json, prefs);
                        return prefs;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading preferences: {ex.Message}");
            }

            return CreateDefaultPreferences();
        }

        public async Task<AppPreferences> LoadPreferencesAsync()
        {
            try
            {
                if (File.Exists(PreferencesFilePath))
                {
                    var json = await File.ReadAllTextAsync(PreferencesFilePath);
                    var prefs = JsonSerializer.Deserialize<AppPreferences>(json, LoadOptions);
                    if (prefs != null)
                    {
                        EnsureInitialized(prefs);
                        ApplyLegacyUpdatePreferenceMigrations(json, prefs);
                        return prefs;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading preferences: {ex.Message}");
            }

            return CreateDefaultPreferences();
        }

        private static void ApplyLegacyUpdatePreferenceMigrations(string rawJson, AppPreferences prefs)
        {
            try
            {
                using var doc = JsonDocument.Parse(rawJson);
                if (!doc.RootElement.TryGetProperty("general", out var g))
                    return;
                if (!g.TryGetProperty("automaticallyInstallUpdates", out _) && prefs.General.CheckForUpdates)
                    prefs.General.AutomaticallyInstallUpdates = true;
            }
            catch
            {
                // ignore
            }
        }

        public void SavePreferencesSync(AppPreferences preferences)
        {
            try
            {
                EnsureInitialized(preferences);
                var json = JsonSerializer.Serialize(preferences, SaveOptions);
                File.WriteAllText(PreferencesFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving preferences: {ex.Message}");
            }
        }

        public async Task SavePreferencesAsync(AppPreferences preferences)
        {
            try
            {
                EnsureInitialized(preferences);
                var json = JsonSerializer.Serialize(preferences, SaveOptions);
                await File.WriteAllTextAsync(PreferencesFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving preferences: {ex.Message}");
            }
        }

        public static void EnsureInitialized(AppPreferences preferences)
        {
            preferences.General ??= new GeneralPreferences();
            preferences.Sidebar ??= new SidebarPreferences();
            preferences.Playback ??= new PlaybackPreferences();
            preferences.Library ??= new LibraryPreferences();
            preferences.Theme ??= new ThemePreferences();
            preferences.General.Language ??= "en-system";
            preferences.General.UiFontFamily ??= "";
            preferences.General.UiFontSize = Math.Clamp(
                preferences.General.UiFontSize,
                GeneralPreferences.UiFontSizeMin,
                GeneralPreferences.UiFontSizeMax);
            preferences.Playback.CrossfadeSeconds = Math.Clamp(preferences.Playback.CrossfadeSeconds, 0, 15);
            if (preferences.Playback.CrossfadeSeconds <= 0)
                preferences.Playback.CrossfadeRampSeconds = 0;
            else
                preferences.Playback.CrossfadeRampSeconds = Math.Clamp(preferences.Playback.CrossfadeRampSeconds, 0, 120d);
            if (!Enum.IsDefined(typeof(AudioOutputBackend), preferences.Playback.AudioBackend))
                preferences.Playback.AudioBackend = AudioOutputBackend.WasapiShared;
            preferences.Playback.OutputSampleRateHz =
                PlaybackResampler.NormalizeOutputSampleRateHz(preferences.Playback.OutputSampleRateHz);
            preferences.Playback.OutputBits = PlaybackOutputBitsUtil.Normalize(preferences.Playback.OutputBits);
        }

        public static AppPreferences CreateDefaultPreferences()
        {
            return new AppPreferences
            {
                General = new GeneralPreferences(),
                Sidebar = new SidebarPreferences(),
                Playback = new PlaybackPreferences(),
                Library = new LibraryPreferences(),
                Theme = new ThemePreferences()
            };
        }
    }
}
