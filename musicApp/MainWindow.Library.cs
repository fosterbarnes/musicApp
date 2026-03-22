using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MusicApp.Dialogs;
using MusicApp.Helpers;

namespace MusicApp
{
    public partial class MainWindow
    {
        private async Task AddMusicFolderAsync()
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select a folder containing music files"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                await LoadMusicFromFolderAsync(dialog.SelectedPath, true);
            }
        }

        private async Task RescanLibraryAsync()
        {
            try
            {
                var musicFolders = await libraryManager.GetMusicFoldersAsync();
                if (musicFolders == null || musicFolders.Count == 0)
                {
                    MessageDialog.Show(this, "No Folders", "No music folders have been added yet.", MessageDialog.Buttons.Ok);
                    return;
                }

                await Task.Run(() => AlbumArtCacheManager.InvalidateAll());

                var totalNewTracks = 0;
                foreach (var folderPath in musicFolders)
                {
                    if (Directory.Exists(folderPath))
                    {
                        await LoadMusicFromFolderAsync(folderPath, false);
                        totalNewTracks += allTracks.Count(t => t.FilePath.StartsWith(folderPath));
                    }
                }

                UpdateUI();
                MessageDialog.Show(this, "Library Updated", $"Library re-scanned. Found {totalNewTracks} total tracks across all folders.", MessageDialog.Buttons.Ok);
            }
            catch (Exception ex)
            {
                MessageDialog.Show(this, "Error", $"Error re-scanning library: {ex.Message}", MessageDialog.Buttons.Ok);
            }
        }

        private async Task RemoveMusicFolderAsync()
        {
            try
            {
                var musicFolders = await libraryManager.GetMusicFoldersAsync();
                if (musicFolders == null || musicFolders.Count == 0)
                {
                    MessageDialog.Show(this, "No Folders", "No music folders have been added yet.", MessageDialog.Buttons.Ok);
                    return;
                }

                var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Select a folder to remove from the library",
                    ShowNewFolderButton = false
                };

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var folderToRemove = dialog.SelectedPath;
                    if (musicFolders.Contains(folderToRemove))
                    {
                        var tracksToRemove = allTracks.Where(t => t.FilePath.StartsWith(folderToRemove)).ToList();
                        foreach (var track in tracksToRemove)
                        {
                            RemoveTrackFromCollections(track, includeShuffled: false);
                        }

                        foreach (var playlist in playlists)
                        {
                            var playlistTracksToRemove = playlist.Tracks.Where(t => t.FilePath.StartsWith(folderToRemove)).ToList();
                            foreach (var track in playlistTracksToRemove)
                            {
                                playlist.RemoveTrack(track);
                            }
                        }

                        await libraryManager.RemoveMusicFolderAsync(folderToRemove);
                        await libraryManager.RemoveFolderFromCacheAsync(folderToRemove);

                        UpdateUI();
                        UpdateShuffledTracks();
                        RefreshVisibleViews();
                        UpdateStatusBar();

                        MessageDialog.Show(this, "Folder Removed", $"Folder '{folderToRemove}' and {tracksToRemove.Count} tracks removed from library.", MessageDialog.Buttons.Ok);
                    }
                    else
                    {
                        MessageDialog.Show(this, "Folder Not Found", $"Folder '{folderToRemove}' not found in library.", MessageDialog.Buttons.Ok);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageDialog.Show(this, "Error", $"Error removing folder: {ex.Message}", MessageDialog.Buttons.Ok);
            }
        }
    }
}
