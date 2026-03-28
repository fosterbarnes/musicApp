using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using musicApp.Constants;
using musicApp.Helpers;
using NAudio.Wave;

namespace musicApp
{
    public partial class MainWindow
    {
        private void CleanupAudioObjects()
        {
            try
            {
                LogDebug("Cleaning up audio objects...");
                titleBarPlayer.IsPlaying = false;
                TeardownCrossfadePlaybackState();

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

                _sessionVolumeProvider = null;
                ClearCrossfadeMixerReferences();

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

                if (_crossfadeOverlapActive)
                {
                    try
                    {
                        if (IsOutgoingReaderEnded())
                            CompleteCrossfadeAndPromoteIncoming();
                        else
                            CancelCrossfadeIncomingBranch();
                    }
                    catch
                    {
                        // ignore
                    }
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

                if (HasContextualPlaybackQueue())
                {
                    if (TryAdvanceContextualSessionMovingFinishedToHistory(out var nextContext) &&
                        nextContext != null)
                    {
                        PlayTrack(nextContext);
                        RefreshVisibleViews();
                    }
                    else
                    {
                        CleanupAudioObjects();
                        ClearContextualPlaybackQueue();
                    }
                    return;
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
            if (string.IsNullOrWhiteSpace(filePath))
                return default;

            AudioFileReader? readerForPosition = audioFileReader;

            if (_crossfadeOverlapActive && _incomingAudioFileReader != null)
            {
                if (currentTrack != null && string.Equals(currentTrack.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                    readerForPosition = _incomingAudioFileReader;
                else if (_songOutgoingDuringCrossfade != null &&
                         string.Equals(_songOutgoingDuringCrossfade.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                    readerForPosition = audioFileReader;
                else
                    return default;
            }
            else
            {
                if (currentTrack == null ||
                    !string.Equals(currentTrack.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                    return default;
            }

            if (readerForPosition == null || waveOut == null)
                return default;

            var position = readerForPosition.CurrentTime;
            var wasPlaying = waveOut.PlaybackState == PlaybackState.Playing;

            try
            {
                titleBarPlayer.IsPlaying = false;
                TeardownCrossfadePlaybackState();
                waveOut.PlaybackStopped -= WaveOut_PlaybackStopped;
                waveOut.Stop();
                waveOut.Dispose();
                waveOut = null;
                if (audioFileReader != null)
                {
                    audioFileReader.Dispose();
                    audioFileReader = null;
                }
                ClearCrossfadeMixerReferences();
                _sessionVolumeProvider = null;
                TitleBarSetAudioObjects(null, null);
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
                TeardownCrossfadePlaybackState();
                RefreshPlaybackAudioPreferenceFields();
                audioFileReader = new AudioFileReader(path);
                waveOut = AudioOutputDeviceFactory.Create(_cachedAudioBackend);
                waveOut.PlaybackStopped += WaveOut_PlaybackStopped;
                waveOut.Init(CreatePlaybackInitChain(audioFileReader, path));

                if (release.Position > TimeSpan.Zero && release.Position < audioFileReader.TotalTime)
                    audioFileReader.CurrentTime = release.Position;

                TitleBarSetAudioObjects(waveOut, audioFileReader);
                _crossfadeOverlapStartedForThisOutgoing = false;
                EnsureCrossfadePollTimer();

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

        private void RefreshPlaybackAudioPreferenceFields()
        {
            var prefs = PreferencesManager.Instance.LoadPreferencesSync();
            PreferencesManager.EnsureInitialized(prefs);
            _cachedAudioBackend = prefs.Playback.AudioBackend;
            _useSoftwareSessionVolume = prefs.Playback.UseSoftwareSessionVolume;
            _cachedOutputSampleRateHz = prefs.Playback.OutputSampleRateHz;
            _cachedOutputBits = prefs.Playback.OutputBits;
        }

        private void TitleBarSetAudioObjects(IWavePlayer? w, AudioFileReader? r)
        {
            titleBarPlayer.SetAudioObjects(w, r, _useSoftwareSessionVolume);
        }

        private float GetTitleBarOutputVolumeLinear()
        {
            if (titleBarPlayer.IsMuted)
                return 0f;
            return (float)(titleBarPlayer.Volume / 100.0);
        }

        private void RecreateAudioOutputForPreferencesChange()
        {
            if (currentTrack == null)
                return;
            var path = currentTrack.FilePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            TimeSpan position;
            bool wasPlaying;
            try
            {
                position = audioFileReader?.CurrentTime ?? TimeSpan.Zero;
                wasPlaying = waveOut?.PlaybackState == PlaybackState.Playing;
            }
            catch
            {
                position = TimeSpan.Zero;
                wasPlaying = false;
            }

            TeardownCrossfadePlaybackState();
            try
            {
                if (waveOut != null)
                {
                    waveOut.PlaybackStopped -= WaveOut_PlaybackStopped;
                    waveOut.Stop();
                    waveOut.Dispose();
                    waveOut = null;
                }
            }
            catch { }

            try
            {
                audioFileReader?.Dispose();
            }
            catch { }
            audioFileReader = null;
            ClearCrossfadeMixerReferences();
            _sessionVolumeProvider = null;

            try
            {
                RefreshPlaybackAudioPreferenceFields();
                audioFileReader = new AudioFileReader(path);
                waveOut = AudioOutputDeviceFactory.Create(_cachedAudioBackend);
                waveOut.PlaybackStopped += WaveOut_PlaybackStopped;
                waveOut.Init(CreatePlaybackInitChain(audioFileReader, path));

                if (position > TimeSpan.Zero && position < audioFileReader.TotalTime)
                    audioFileReader.CurrentTime = position;

                TitleBarSetAudioObjects(waveOut, audioFileReader);
                _crossfadeOverlapStartedForThisOutgoing = false;
                EnsureCrossfadePollTimer();

                if (wasPlaying)
                {
                    waveOut.Play();
                    titleBarPlayer.IsPlaying = true;
                }
                else
                    titleBarPlayer.IsPlaying = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RecreateAudioOutputForPreferencesChange: {ex.Message}");
            }
        }
    }
}
