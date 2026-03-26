using System;
using System.IO;
using System.Windows.Threading;
using musicApp.Helpers;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace musicApp
{
    public partial class MainWindow
    {
        private int _crossfadeSecondsPreferred;
        private double _crossfadeRampSecondsPreferred = 2d;
        private const double CrossfadeRemainingTriggerEpsilonSec = 0.001;
        private DispatcherTimer? _crossfadePollTimer;
        private MixingSampleProvider? _mixingProvider;
        private VolumeSampleProvider? _outgoingVolumeSampleProvider;
        private VolumeSampleProvider? _incomingVolumeSampleProvider;
        private AudioFileReader? _incomingAudioFileReader;
        private Song? _pendingIncomingSong;
        private Song? _songOutgoingDuringCrossfade;
        private bool _crossfadeOverlapStartedForThisOutgoing;
        private bool _crossfadeOverlapActive;
        private bool _crossfadeRampComplete;
        private bool _completingCrossfadePromotion;
        private DateTime _crossfadeRampStartUtc;
        private double _crossfadeRampDurationSeconds;

        private void ApplyCrossfadePreferenceSeconds(int seconds)
        {
            _crossfadeSecondsPreferred = Math.Clamp(seconds, 0, 15);
            if (_crossfadeSecondsPreferred == 0)
            {
                CancelCrossfadeIncomingBranch();
                StopCrossfadePollTimer();
            }
            else
                EnsureCrossfadePollTimer();
        }

        private void ApplyCrossfadeRampSeconds(double seconds)
        {
            _crossfadeRampSecondsPreferred = Math.Clamp(seconds, 0, 120d);
        }

        private void TeardownCrossfadePlaybackState()
        {
            StopCrossfadePollTimer();
            _crossfadeOverlapStartedForThisOutgoing = false;
            _crossfadeOverlapActive = false;
            _crossfadeRampComplete = false;
            try
            {
                if (_incomingVolumeSampleProvider != null && _mixingProvider != null)
                    _mixingProvider.RemoveMixerInput(_incomingVolumeSampleProvider);
            }
            catch { }
            _incomingVolumeSampleProvider = null;
            try { _incomingAudioFileReader?.Dispose(); } catch { }
            _incomingAudioFileReader = null;
            _pendingIncomingSong = null;
            _songOutgoingDuringCrossfade = null;
        }

        private void ClearCrossfadeMixerReferences()
        {
            _mixingProvider = null;
            _outgoingVolumeSampleProvider = null;
        }

        private IWaveProvider CreatePlaybackInitChain(AudioFileReader reader, string pathForBuild)
        {
            IWaveProvider core;
            if (_crossfadeSecondsPreferred <= 0)
            {
                _mixingProvider = null;
                _outgoingVolumeSampleProvider = null;
                core = BuildPlaybackOutput(reader, pathForBuild);
            }
            else
            {
                var outgoingSp = PlaybackResampler.ResampleIfNeeded(
                    reader.ToSampleProvider(),
                    reader.WaveFormat,
                    _cachedOutputSampleRateHz);
                _outgoingVolumeSampleProvider = new VolumeSampleProvider(outgoingSp) { Volume = 1f };
                _mixingProvider = new MixingSampleProvider(_outgoingVolumeSampleProvider.WaveFormat);
                _mixingProvider.AddMixerInput(_outgoingVolumeSampleProvider);
                core = new SampleToWaveProvider(_mixingProvider);
            }

            _sessionVolumeProvider = null;
            IWaveProvider ieeeFloatChain;
            if (_useSoftwareSessionVolume)
            {
                _sessionVolumeProvider = new VolumeSampleProvider(core.ToSampleProvider())
                {
                    Volume = GetTitleBarOutputVolumeLinear()
                };
                ieeeFloatChain = new SampleToWaveProvider(_sessionVolumeProvider);
            }
            else
                ieeeFloatChain = core;

            return PlaybackOutputBitsUtil.ApplyToIeeeFloatChain(_cachedOutputBits, ieeeFloatChain);
        }

        private void EnsureCrossfadePollTimer()
        {
            if (_crossfadeSecondsPreferred <= 0)
            {
                StopCrossfadePollTimer();
                return;
            }

            if (_crossfadePollTimer == null)
            {
                _crossfadePollTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(75)
                };
                _crossfadePollTimer.Tick += CrossfadePollTimer_Tick;
            }

            _crossfadePollTimer.Start();
        }

        private void StopCrossfadePollTimer() => _crossfadePollTimer?.Stop();

        private void CrossfadePollTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (_crossfadeOverlapActive)
                {
                    AdvanceCrossfadeRamp();
                    if (IsOutgoingReaderEnded())
                        CompleteCrossfadeAndPromoteIncoming();
                    return;
                }

                TryBeginCrossfadeOverlap();
            }
            catch
            {
                // ignore tick errors
            }
        }

        private bool IsOutgoingReaderEnded()
        {
            if (audioFileReader == null)
                return true;
            try
            {
                var total = audioFileReader.TotalTime.TotalSeconds;
                if (total <= 0)
                    return false;
                return audioFileReader.CurrentTime.TotalSeconds >= total - 0.07;
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void TryBeginCrossfadeOverlap()
        {
            if (_crossfadeSecondsPreferred <= 0 || waveOut == null || audioFileReader == null)
                return;
            if (waveOut.PlaybackState != PlaybackState.Playing)
                return;
            if (isManualNavigation)
                return;
            if (_crossfadeOverlapStartedForThisOutgoing || _crossfadeOverlapActive)
                return;

            double totalSec;
            double remaining;
            try
            {
                var _ = audioFileReader.TotalTime;
                totalSec = audioFileReader.TotalTime.TotalSeconds;
                remaining = (audioFileReader.TotalTime - audioFileReader.CurrentTime).TotalSeconds;
            }
            catch
            {
                return;
            }

            if (totalSec <= 0)
                return;
            if (remaining > _crossfadeSecondsPreferred + CrossfadeRemainingTriggerEpsilonSec)
                return;

            var idx = GetCurrentTrackIndex();
            var next = GetTrackFromCurrentQueue(idx + 1);
            if (next == null || string.IsNullOrEmpty(next.FilePath) || !File.Exists(next.FilePath))
                return;
            if (_mixingProvider == null)
                return;

            BeginCrossfadeOverlap(next);
        }

        private void BeginCrossfadeOverlap(Song next)
        {
            try
            {
                _incomingAudioFileReader = new AudioFileReader(next.FilePath);
                var incomingSp = PlaybackResampler.ResampleIfNeeded(
                    _incomingAudioFileReader.ToSampleProvider(),
                    _incomingAudioFileReader.WaveFormat,
                    _cachedOutputSampleRateHz);
                _incomingVolumeSampleProvider = new VolumeSampleProvider(incomingSp) { Volume = 0f };
                _mixingProvider!.AddMixerInput(_incomingVolumeSampleProvider);
            }
            catch
            {
                try { _incomingAudioFileReader?.Dispose(); } catch { }
                _incomingAudioFileReader = null;
                _incomingVolumeSampleProvider = null;
                return;
            }

            _pendingIncomingSong = next;
            _crossfadeOverlapActive = true;
            _crossfadeOverlapStartedForThisOutgoing = true;
            _crossfadeRampComplete = false;
            double remainingAtStart;
            try
            {
                remainingAtStart = (audioFileReader!.TotalTime - audioFileReader.CurrentTime).TotalSeconds;
            }
            catch
            {
                remainingAtStart = _crossfadeSecondsPreferred;
            }

            _crossfadeRampDurationSeconds = CrossfadeParameters.ClampRampToOverlap(
                _crossfadeRampSecondsPreferred,
                remainingAtStart);
            _crossfadeRampStartUtc = DateTime.UtcNow;

            _songOutgoingDuringCrossfade = currentTrack;
            currentTrack = next;
            SyncCurrentTrackIndices(next);

            var art = AlbumArtLoader.LoadAlbumArt(next);
            titleBarPlayer.SetTrackInfo(next.Title, next.Artist, next.Album, art);
            TitleBarSetAudioObjects(waveOut, _incomingAudioFileReader);
            RefreshVisibleViews();
        }

        private void AdvanceCrossfadeRamp()
        {
            if (!_crossfadeOverlapActive || _outgoingVolumeSampleProvider == null || _incomingVolumeSampleProvider == null)
                return;
            if (_crossfadeRampComplete)
            {
                _outgoingVolumeSampleProvider.Volume = 0f;
                _incomingVolumeSampleProvider.Volume = 1f;
                return;
            }

            if (_crossfadeRampDurationSeconds <= 0)
            {
                _crossfadeRampComplete = true;
                _outgoingVolumeSampleProvider.Volume = 0f;
                _incomingVolumeSampleProvider.Volume = 1f;
                return;
            }

            var elapsed = (DateTime.UtcNow - _crossfadeRampStartUtc).TotalSeconds;
            var t = (float)Math.Min(1.0, elapsed / _crossfadeRampDurationSeconds);
            _outgoingVolumeSampleProvider.Volume = CrossfadeParameters.LinearOutgoingVolume(t);
            _incomingVolumeSampleProvider.Volume = CrossfadeParameters.LinearIncomingVolume(t);
            if (t >= 1f)
            {
                _crossfadeRampComplete = true;
                _outgoingVolumeSampleProvider.Volume = 0f;
                _incomingVolumeSampleProvider.Volume = 1f;
            }
        }

        private void CompleteCrossfadeAndPromoteIncoming()
        {
            if (!_crossfadeOverlapActive || _pendingIncomingSong == null || _completingCrossfadePromotion)
                return;

            _completingCrossfadePromotion = true;
            try
            {
                var promoted = _pendingIncomingSong;
                _songOutgoingDuringCrossfade = null;

                try
                {
                    if (_mixingProvider != null && _outgoingVolumeSampleProvider != null)
                        _mixingProvider.RemoveMixerInput(_outgoingVolumeSampleProvider);
                }
                catch { }

                try { audioFileReader?.Dispose(); } catch { }

                audioFileReader = _incomingAudioFileReader;
                _incomingAudioFileReader = null;
                _outgoingVolumeSampleProvider = _incomingVolumeSampleProvider;
                _incomingVolumeSampleProvider = null;
                if (_outgoingVolumeSampleProvider != null)
                    _outgoingVolumeSampleProvider.Volume = 1f;

                _pendingIncomingSong = null;
                _crossfadeOverlapActive = false;
                _crossfadeOverlapStartedForThisOutgoing = false;
                _crossfadeRampComplete = false;

                currentTrack = promoted;
                SyncCurrentTrackIndices(promoted);
                UpdateShuffleIndicesAfterTrackChange(promoted);

                var albumArt = AlbumArtLoader.LoadAlbumArt(promoted);
                titleBarPlayer.SetTrackInfo(promoted.Title, promoted.Artist, promoted.Album, albumArt);
                TitleBarSetAudioObjects(waveOut, audioFileReader);

                AddToRecentlyPlayed(promoted);
                RefreshVisibleViews();
            }
            finally
            {
                _completingCrossfadePromotion = false;
            }
        }

        private void CancelCrossfadeIncomingBranch()
        {
            if (!_crossfadeOverlapActive && _incomingVolumeSampleProvider == null)
                return;
            try
            {
                if (_incomingVolumeSampleProvider != null && _mixingProvider != null)
                    _mixingProvider.RemoveMixerInput(_incomingVolumeSampleProvider);
            }
            catch { }
            try { _incomingAudioFileReader?.Dispose(); } catch { }
            _incomingAudioFileReader = null;
            _incomingVolumeSampleProvider = null;
            _pendingIncomingSong = null;
            _crossfadeOverlapActive = false;
            _crossfadeOverlapStartedForThisOutgoing = false;
            _crossfadeRampComplete = false;
            if (_outgoingVolumeSampleProvider != null)
                _outgoingVolumeSampleProvider.Volume = 1f;

            if (_songOutgoingDuringCrossfade != null)
            {
                var back = _songOutgoingDuringCrossfade;
                _songOutgoingDuringCrossfade = null;
                currentTrack = back;
                SyncCurrentTrackIndices(back);
                try
                {
                    var artBack = AlbumArtLoader.LoadAlbumArt(back);
                    titleBarPlayer.SetTrackInfo(back.Title, back.Artist, back.Album, artBack);
                    TitleBarSetAudioObjects(waveOut, audioFileReader);
                }
                catch
                {
                    // ignore
                }

                RefreshVisibleViews();
            }
        }

        private void OnTitleBarPlaybackPositionCommitted(object? sender, EventArgs e)
        {
            if (_crossfadeOverlapActive)
            {
                CancelCrossfadeIncomingBranch();
            }

            _crossfadeOverlapStartedForThisOutgoing = false;
        }
    }
}
