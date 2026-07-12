using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using musicApp.Constants;
using musicApp.Helpers;

namespace musicApp
{
    public class SettingsManager
    {
        private static readonly string SettingsFilePath = AppPaths.SettingsPath;

        public class AppSettings
        {
            public WindowStateSettings WindowState { get; set; } = new WindowStateSettings();
            public PlayerSettings Player { get; set; } = new PlayerSettings();
            public string LastActiveView { get; set; } = "Library";
        }

        public class PlayerSettings
        {
            public bool IsShuffleEnabled { get; set; } = false;
            public RepeatMode RepeatMode { get; set; } = RepeatMode.Off;

            /// <summary>Title bar volume slider 0–100; omitted in older settings files means default 100.</summary>
            public double? TitleBarVolume0To100 { get; set; }
        }

        public enum RepeatMode
        {
            Off = 0,
            All = 1,
            One = 2
        }

        public class WindowStateSettings
        {
            public bool IsMaximized { get; set; } = false;
            public double Width { get; set; } = 1200;
            public double Height { get; set; } = 700;
            public double Left { get; set; } = 100;
            public double Top { get; set; } = 100;
            public double SidebarWidth { get; set; } = UILayoutConstants.SidebarMinWidth;
            public Dictionary<string, double> SongsViewColumnWidths { get; set; } = new Dictionary<string, double>();
            public Dictionary<string, Dictionary<string, double>> ColumnWidths { get; set; } = new Dictionary<string, Dictionary<string, double>>();
            public Dictionary<string, List<string>> ColumnVisibility { get; set; } = new Dictionary<string, List<string>>();
        }

        private static SettingsManager? _instance;
        private static readonly object _lock = new object();

        public static SettingsManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new SettingsManager();
                    }
                }
                return _instance;
            }
        }

        private SettingsManager()
        {
            AppPaths.EnsureAppDataDirectory();
        }

        /// <summary>
        /// Older builds defaulted <see cref="WindowStateSettings.SidebarWidth"/> to 250 while the UI min width was 180.
        /// Normalize that legacy default so saved settings match the narrow sidebar (without wiping custom widths).
        /// </summary>
        private static void MigrateLegacySidebarWidth(WindowStateSettings ws)
        {
            const double legacyDefaultSidebarWidth = 250;
            if (Math.Abs(ws.SidebarWidth - legacyDefaultSidebarWidth) < 0.5)
                ws.SidebarWidth = UILayoutConstants.SidebarMinWidth;
        }

        #region Settings Management

        public AppSettings LoadSettingsSync()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    var settings = JsonSerializer.Deserialize<AppSettings>(json, options);

                    if (settings != null)
                    {
                        settings.Player ??= new PlayerSettings();
                        settings.WindowState ??= new WindowStateSettings();
                        MigrateLegacySidebarWidth(settings.WindowState);
                        return settings;
                    }
                }
            }
            catch
            {
            }

            return new AppSettings
            {
                Player = new PlayerSettings(),
                WindowState = new WindowStateSettings()
            };
        }

        public async Task<AppSettings> LoadSettingsAsync()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = await File.ReadAllTextAsync(SettingsFilePath);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    var settings = JsonSerializer.Deserialize<AppSettings>(json, options);

                    if (settings != null)
                    {
                        settings.Player ??= new PlayerSettings();
                        settings.WindowState ??= new WindowStateSettings();
                        MigrateLegacySidebarWidth(settings.WindowState);
                        return settings;
                    }
                }
            }
            catch
            {
            }

            return new AppSettings
            {
                Player = new PlayerSettings(),
                WindowState = new WindowStateSettings()
            };
        }

        public async Task SaveSettingsAsync(AppSettings settings)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                var json = JsonSerializer.Serialize(settings, options);
                await File.WriteAllTextAsync(SettingsFilePath, json);
            }
            catch
            {
            }
        }

        public void SaveSettingsSync(AppSettings settings)
        {
            AppPaths.EnsureAppDataDirectory();
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(SettingsFilePath, json);
        }

        #endregion

        #region Player Settings

        public async Task<bool> GetShuffleStateAsync()
        {
            var settings = await LoadSettingsAsync();
            return settings.Player?.IsShuffleEnabled ?? false;
        }

        public async Task SetShuffleStateAsync(bool isEnabled)
        {
            var settings = await LoadSettingsAsync();
            if (settings.Player != null)
            {
                settings.Player.IsShuffleEnabled = isEnabled;
                await SaveSettingsAsync(settings);
            }
        }

        public async Task<RepeatMode> GetRepeatModeAsync()
        {
            var settings = await LoadSettingsAsync();
            return settings.Player?.RepeatMode ?? RepeatMode.Off;
        }

        public async Task SetRepeatModeAsync(RepeatMode repeatMode)
        {
            var settings = await LoadSettingsAsync();
            if (settings.Player != null)
            {
                settings.Player.RepeatMode = repeatMode;
                await SaveSettingsAsync(settings);
            }
        }

        public async Task SetTitleBarVolume0To100Async(double volume0To100)
        {
            var settings = await LoadSettingsAsync();
            if (settings.Player != null)
            {
                settings.Player.TitleBarVolume0To100 = Math.Clamp(volume0To100, 0, 100);
                await SaveSettingsAsync(settings);
            }
        }

        public async Task<double> GetTitleBarVolume0To100Async()
        {
            var settings = await LoadSettingsAsync();
            var v = settings.Player?.TitleBarVolume0To100;
            if (!v.HasValue)
                return 100;
            return Math.Clamp(v.Value, 0, 100);
        }

        #endregion
    }
}
