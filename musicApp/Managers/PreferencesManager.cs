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
        private static readonly string AppDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "musicApp");

        private static readonly string PreferencesFilePath = Path.Combine(AppDataPath, "preferences.json");

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
            public bool CheckForUpdates { get; set; }

            public bool AutomaticallyInstallUpdates { get; set; }

            public bool LaunchAppAfterUpdate { get; set; }

            public string Language { get; set; } = "en-system";
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
            EnsureAppDataDirectoryExists();
        }

        private void EnsureAppDataDirectoryExists()
        {
            if (!Directory.Exists(AppDataPath))
            {
                Directory.CreateDirectory(AppDataPath);
            }
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
            preferences.General.Language ??= "en-system";
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
                Playback = new PlaybackPreferences()
            };
        }
    }
}
