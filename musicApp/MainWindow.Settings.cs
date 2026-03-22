using System;
using System.IO;
using MusicApp.Dialogs;

namespace MusicApp
{
    public partial class MainWindow
    {
        private void UpdatePlaylistsView()
        {
            // Intentionally left as placeholder for future playlist-specific refresh behavior.
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

                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "musicApp");

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
                filteredTracks.Clear();
                shuffledTracks.Clear();
                playlists.Clear();
                recentlyPlayed.Clear();

                currentTrack = null;
                currentTrackIndex = -1;
                currentShuffledIndex = -1;
                ClearContextualPlaybackQueue();

                StopPlayback();

                titleBarPlayer.SetTrackInfo("No track selected", "", "");

                appSettings = new SettingsManager.AppSettings();
                windowManager.ResetWindowState();

                RefreshVisibleViews();
                UpdateUI();

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
