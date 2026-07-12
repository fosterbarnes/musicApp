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
        /// <summary>
        /// Disposes output devices/readers only. Keeps contextual queue (
        /// <see cref="MainWindow.ClearContextualPlaybackQueue"/>) so skip/load-next does not drop a Songs/playlist session.
        /// </summary>
        private void CleanupAudioObjects()
        {
            try
            {
                LogDebug("Cleaning up audio objects...");
                TeardownPlaybackOutput();
                ResetToIdleState();
            }
            catch (Exception ex)
            {
                LogDebug($"Error during audio cleanup: {ex.Message}");
            }
        }

        private void TeardownPlaybackOutput()
        {
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
        }

        private void CreateAndBindPlaybackOutput(string filePath, bool autoPlay, TimeSpan? seekTo = null)
        {
            RefreshPlaybackAudioPreferenceFields();
            audioFileReader = new AudioFileReader(filePath);
            waveOut = AudioOutputDeviceFactory.Create(_cachedAudioBackend);
            waveOut.PlaybackStopped += WaveOut_PlaybackStopped;
            waveOut.Init(CreatePlaybackInitChain(audioFileReader, filePath));

            if (seekTo is TimeSpan pos && pos > TimeSpan.Zero && pos < audioFileReader.TotalTime)
                audioFileReader.CurrentTime = pos;

            TitleBarSetAudioObjects(waveOut, audioFileReader);
            _crossfadeOverlapStartedForThisOutgoing = false;
            EnsureCrossfadePollTimer();

            if (autoPlay)
            {
                waveOut.Play();
                titleBarPlayer.IsPlaying = true;
            }
            else
                titleBarPlayer.IsPlaying = false;
        }

        private void UpdateNowPlayingUi(Song track)
        {
            var art = AlbumArtLoader.LoadAlbumArt(track, GetTitleBarAlbumArtTargetPixelSize());
            titleBarPlayer.SetTrackInfo(track.Title, track.Artist, track.Album, art);
            PushMiniPlayerTrack(track);
        }

        private void StopPlayback(bool clearQueue = true)
        {
            isManuallyStopping = true;

            try
            {
                CleanupAudioObjects();
                if (clearQueue)
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

                var repeatMode = titleBarPlayer.RepeatMode;

                if (repeatMode == SettingsManager.RepeatMode.One && currentTrack != null)
                {
                    if (TryRepeatOneRestart())
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
                        return;
                    }

                    if (repeatMode == SettingsManager.RepeatMode.All &&
                        TryWrapContextualForRepeatAll(out var wrapStart) &&
                        wrapStart != null)
                    {
                        PlayTrack(wrapStart);
                        RefreshVisibleViews();
                        return;
                    }

                    CleanupAudioObjects();
                    ClearContextualPlaybackQueue();
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
                else if (repeatMode == SettingsManager.RepeatMode.All)
                {
                    if (TryWrapLibraryForRepeatAll(out var libStart) && libStart != null)
                    {
                        PlayTrack(libStart);
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

        private bool TryRepeatOneRestart()
        {
            try
            {
                if (waveOut == null || audioFileReader == null || currentTrack == null)
                    return false;
                audioFileReader.CurrentTime = TimeSpan.Zero;
                waveOut.Play();
                titleBarPlayer.IsPlaying = true;
                _crossfadeOverlapStartedForThisOutgoing = false;
                EnsureCrossfadePollTimer();
                return true;
            }
            catch (Exception ex)
            {
                LogDebug($"TryRepeatOneRestart failed: {ex.Message}");
                return false;
            }
        }

        private bool TryWrapLibraryForRepeatAll(out Song? startTrack)
        {
            startTrack = null;
            if (filteredTracks == null || filteredTracks.Count == 0)
                return false;

            if (titleBarPlayer.IsShuffleEnabled)
            {
                if (shuffledTracks.Count == 0 ||
                    shuffledTracks.Count != filteredTracks.Count)
                {
                    RegenerateShuffledTracks();
                    if (shuffledTracks.Count == 0)
                        return false;
                }

                currentShuffledIndex = 0;
                startTrack = shuffledTracks[0];
                if (startTrack == null)
                    return false;

                int li = filteredTracks.IndexOf(startTrack);
                if (li < 0)
                {
                    RegenerateShuffledTracks();
                    if (shuffledTracks.Count == 0)
                        return false;
                    currentShuffledIndex = 0;
                    startTrack = shuffledTracks[0];
                    if (startTrack == null)
                        return false;
                    li = filteredTracks.IndexOf(startTrack);
                    if (li < 0)
                        return false;
                }

                currentTrackIndex = li;
            }
            else
            {
                currentTrackIndex = 0;
                currentShuffledIndex = 0;
                startTrack = filteredTracks[0];
            }
            return startTrack != null;
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
                TeardownPlaybackOutput();
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
                CreateAndBindPlaybackOutput(path, autoPlay: release.WasPlaying, seekTo: release.Position);
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
            if (_miniPlayerWindow != null && _miniPlayerWindow.IsVisible)
                _miniPlayerWindow.SetAudioObjects(w, r);
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

            TeardownPlaybackOutput();

            try
            {
                CreateAndBindPlaybackOutput(path, autoPlay: wasPlaying, seekTo: position);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RecreateAudioOutputForPreferencesChange: {ex.Message}");
            }
        }
    }
}
