using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using musicApp.Dialogs;
using musicApp.Helpers;

namespace musicApp
{
    public partial class MainWindow
    {
        public void ApplyPlaybackPreferences()
        {
            var prefs = PreferencesManager.Instance.LoadPreferencesSync();
            PreferencesManager.EnsureInitialized(prefs);
            var playback = prefs.Playback;
            _player.ApplySettings(new Playback.EngineSettings(
                playback.AudioBackend,
                playback.UseSoftwareSessionVolume,
                playback.OutputSampleRateHz,
                playback.OutputBits,
                Math.Clamp(playback.CrossfadeSeconds, 0, 15),
                Math.Clamp(playback.CrossfadeRampSeconds, 0, 120d)));
        }

        public List<string> CopyLibraryFilePathsForLoudnormStats()
        {
            var paths = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in allTracks)
            {
                var path = t.FilePath;
                if (string.IsNullOrWhiteSpace(path) || !seen.Add(path))
                    continue;
                try
                {
                    if (File.Exists(path))
                        paths.Add(path);
                }
                catch { }
            }
            return paths;
        }

        public void ApplySidebarPreferences()
        {
            var prefs = PreferencesManager.Instance.LoadPreferencesSync();
            PreferencesManager.EnsureInitialized(prefs);
            var s = prefs.Sidebar;
            btnAddFolder.Visibility = s.ShowAddMusic ? Visibility.Visible : Visibility.Collapsed;
            btnRescanLibrary.Visibility = s.ShowRescanLibrary ? Visibility.Visible : Visibility.Collapsed;
            btnRemoveFolder.Visibility = s.ShowRemoveMusic ? Visibility.Visible : Visibility.Collapsed;
            btnClearSettings.Visibility = s.ShowClearSettings ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Reloads <c>settings.json</c> into the live session after Settings import (window layout + player UI).
        /// </summary>
        public void ApplyImportedSettingsFromDisk()
        {
            try
            {
                appSettings = settingsManager.LoadSettingsSync() ?? new SettingsManager.AppSettings();
                RestoreWindowState();
            }
            catch
            {
                // Keep current in-memory settings if reload fails.
            }

            ApplySidebarPreferences();
            ApplyPlaybackPreferences();
            _ = titleBarPlayer.ReloadPlayerSettingsFromDiskAsync();
        }

        public void RunClearSettingsFromSettings() => ClearSettings();

        private void UpdatePlaylistsView()
        {
            if (playlistsViewControl != null)
                playlistsViewControl.Playlists = playlists;
        }

        private void ClearSettings()
        {
            try
            {
                var result = MessageDialog.Show(this, "Clear Settings",
                    "This will clear all settings and return the app to a clean state. This action cannot be undone.\n\n" +
                    "The following will be cleared:\n" +
                    "• Music library cache\n" +
                    "• Recently played history\n" +
                    "• Playlists\n" +
                    "• Music folders\n" +
                    "• Window settings\n\n" +
                    "Are you sure you want to continue?",
                    MessageDialog.Buttons.YesNo);

                if (result != true)
                {
                    return;
                }

                var appDataPath = Helpers.AppPaths.AppDataDirectory;

                if (!Directory.Exists(appDataPath))
                {
                    MessageDialog.Show(this, "No Settings", "No settings found to clear.", MessageDialog.Buttons.Ok);
                    return;
                }

                var jsonFiles = Directory.GetFiles(appDataPath, "*.json", SearchOption.TopDirectoryOnly);

                if (jsonFiles.Length == 0)
                {
                    MessageDialog.Show(this, "No Settings", "No settings files found to clear.", MessageDialog.Buttons.Ok);
                    return;
                }

                int movedFiles = 0;
                foreach (var file in jsonFiles)
                {
                    try
                    {
                        if (Helpers.RecycleBinHelper.MoveFileToRecycleBin(file))
                        {
                            movedFiles++;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"Error moving file {file} to recycle bin: {ex.Message}");
                    }
                }

                allTracks.Clear();
                ResetLibraryPathRegistry();
                filteredTracks.Clear();
                playlists.Clear();
                recentlyPlayed.Clear();
                _player.ClearAll();

                var miniPlayer = _miniPlayerWindow;
                _miniPlayerWindow = null;
                miniPlayer?.Close();

                appSettings = new SettingsManager.AppSettings();
                windowManager.ResetWindowState();

                RefreshVisibleViews();
                UpdateUI();

                progressBarFill.Visibility = Visibility.Collapsed;
                progressBarFill.Width = 0;
                UpdateStatusBar();

                ApplySidebarPreferences();
                ApplyPlaybackPreferences();

                MessageDialog.Show(this, "Settings Cleared",
                    $"Successfully cleared {movedFiles} settings files.\n\n" +
                    "The app has been reset to a clean state.",
                    MessageDialog.Buttons.Ok);
            }
            catch (Exception ex)
            {
                MessageDialog.Show(this, "Error", $"Error clearing settings: {ex.Message}", MessageDialog.Buttons.Ok);
            }
        }
    }
}
