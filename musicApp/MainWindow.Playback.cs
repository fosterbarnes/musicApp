using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using MusicApp.Constants;
using NAudio.Wave;

namespace MusicApp
{
    public partial class MainWindow
    {
        private void CleanupAudioObjects()
        {
            try
            {
                LogDebug("Cleaning up audio objects...");
                titleBarPlayer.IsPlaying = false;

                if (waveOut != null)
                {
                    LogDebug("Removing PlaybackStopped handler and stopping waveOut");
                    waveOut.PlaybackStopped -= WaveOut_PlaybackStopped;
                    waveOut.Stop();
                    waveOut.Dispose();
                    waveOut = null;
                }

                if (audioFileReader != null)
                {
                    LogDebug("Disposing audioFileReader");
                    audioFileReader.Dispose();
                    audioFileReader = null;
                }

                ResetToIdleState();
            }
            catch (Exception ex)
            {
                LogDebug($"Error during audio cleanup: {ex.Message}");
            }
        }

        private void StopPlayback()
        {
            isManuallyStopping = true;

            try
            {
                CleanupAudioObjects();
                ClearContextualPlaybackQueue();
            }
            finally
            {
                Task.Delay(UILayoutConstants.ManualNavigationResetDelayMs).ContinueWith(_ => isManuallyStopping = false);
            }
        }

        private void WaveOut_PlaybackStopped(object? sender, StoppedEventArgs e)
        {
            try
            {
                if (isManuallyStopping || waveOut == null || audioFileReader == null)
                {
                    return;
                }

                try
                {
                    var _ = audioFileReader.TotalTime;
                }
                catch (Exception)
                {
                    return;
                }

                var currentQueue = GetCurrentPlayQueue();
                var currentIndex = GetCurrentTrackIndex();

                if (currentQueue == null || currentQueue.Count == 0)
                {
                    CleanupAudioObjects();
                    ClearContextualPlaybackQueue();
                    return;
                }

                if (currentIndex < 0 || currentIndex >= currentQueue.Count)
                {
                    currentIndex = 0;
                }

                if (currentIndex < currentQueue.Count - 1)
                {
                    var nextTrack = GetTrackFromCurrentQueue(currentIndex + 1);
                    if (nextTrack != null)
                    {
                        PlayTrack(nextTrack);
                        RefreshVisibleViews();
                    }
                    else
                    {
                        CleanupAudioObjects();
                        ClearContextualPlaybackQueue();
                    }
                }
                else
                {
                    CleanupAudioObjects();
                    ClearContextualPlaybackQueue();
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error in WaveOut_PlaybackStopped: {ex.Message}");
                try
                {
                    CleanupAudioObjects();
                    ClearContextualPlaybackQueue();
                }
                catch (Exception stopEx)
                {
                    LogDebug($"Error stopping playback: {stopEx.Message}");
                }
            }
        }

        internal MetadataAudioReleaseResult ReleasePlaybackForMetadataWrite(string filePath)
        {
            if (currentTrack == null ||
                string.IsNullOrWhiteSpace(filePath) ||
                !string.Equals(currentTrack.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                return default;
            }

            if (audioFileReader == null || waveOut == null)
                return default;

            var position = audioFileReader.CurrentTime;
            var wasPlaying = waveOut.PlaybackState == PlaybackState.Playing;

            try
            {
                titleBarPlayer.IsPlaying = false;
                waveOut.PlaybackStopped -= WaveOut_PlaybackStopped;
                waveOut.Stop();
                waveOut.Dispose();
                waveOut = null;
                audioFileReader.Dispose();
                audioFileReader = null;
                titleBarPlayer.SetAudioObjects(null, null);
            }
            catch (Exception ex)
            {
                LogDebug($"ReleasePlaybackForMetadataWrite: {ex.Message}");
                return default;
            }

            return new MetadataAudioReleaseResult
            {
                ReleasedPlayback = true,
                Position = position,
                WasPlaying = wasPlaying
            };
        }

        internal void RestorePlaybackAfterMetadataWrite(MetadataAudioReleaseResult release)
        {
            if (!release.ReleasedPlayback || currentTrack == null)
                return;

            var path = currentTrack.FilePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            try
            {
                audioFileReader = new AudioFileReader(path);
                waveOut = new WaveOutEvent();
                waveOut.PlaybackStopped += WaveOut_PlaybackStopped;
                waveOut.Init(audioFileReader);

                if (release.Position > TimeSpan.Zero && release.Position < audioFileReader.TotalTime)
                    audioFileReader.CurrentTime = release.Position;

                titleBarPlayer.SetAudioObjects(waveOut, audioFileReader);

                if (release.WasPlaying)
                {
                    waveOut.Play();
                    titleBarPlayer.IsPlaying = true;
                }
                else
                {
                    titleBarPlayer.IsPlaying = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RestorePlaybackAfterMetadataWrite: {ex.Message}");
            }
        }
    }
}
