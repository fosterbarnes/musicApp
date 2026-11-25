using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using musicApp.Helpers;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace musicApp.Playback;

public sealed record EngineSettings(
    AudioOutputBackend Backend,
    bool SoftwareVolume,
    int SampleRateHz,
    PlaybackOutputBits Bits,
    int CrossfadeSeconds,
    double RampSeconds);

/// <summary>
/// Owns the output device and decks. Create and call it on the UI thread: NAudio and the mixer
/// events are marshalled back to the context captured here.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private const int PauseDrainMs = 250;
    private const double ArmLeadSeconds = 5; // Keeps the next file unlocked until shortly before it plays.

    private readonly Func<AudioOutputBackend, IWavePlayer> _createOutput;
    private readonly Func<string, IAudioSource> _openSource;
    private readonly SynchronizationContext? _context;

    private EngineSettings _settings = new(
        AudioOutputBackend.WasapiShared, true, PlaybackResampler.DefaultOutputSampleRateHz,
        PlaybackOutputBitsUtil.Default, 0, 0);
    private IWavePlayer? _output;
    private EventHandler<StoppedEventArgs>? _stoppedHandler;
    private CrossfadeMixer? _mixer;
    private VolumeSampleProvider? _master;
    private PauseGateSampleProvider? _pauseGate;
    private int _pauseVersion;
    private AudioDeck? _current;
    private Song? _releasedSong;
    private Song? _armSong;
    private bool _armOverlap;
    private bool _armOpened;
    private float _volume = 1f;
    private int _generation;
    private int _armVersion;
    private readonly Dictionary<string, int> _heldFiles = new(StringComparer.OrdinalIgnoreCase); // UI thread
    private readonly object _openLock = new();
    private readonly List<AudioDeck> _openedNotArmed = new(); // opened on a worker, not yet armed; under _openLock
    private readonly List<(Task Open, string Path)> _opening = new(); // background opens; UI thread
    private MetadataAudioReleaseResult? _pendingPlaybackRestore; // waiting for the file's last hold to end

    public AudioEngine(
        Func<AudioOutputBackend, IWavePlayer> createOutput,
        Func<string, IAudioSource> openSource,
        SynchronizationContext? context)
    {
        _createOutput = createOutput;
        _openSource = openSource;
        _context = context;
    }

    public event Action<Song>? TrackStarted;
    public event Action? PlaybackEnded;
    public event Action<Exception>? Failed;

    public Song? CurrentSong => _current?.Song ?? _releasedSong;
    public TimeSpan Position => _current?.Position ?? TimeSpan.Zero;
    public TimeSpan Duration => _current?.Duration ?? TimeSpan.Zero;
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing && _pauseGate?.IsOpen != false;
    internal Task PendingArm { get; private set; } = Task.CompletedTask;

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_master != null)
                _master.Volume = _volume;
            else if (_output != null)
                _output.Volume = _volume;
        }
    }

    public void Load(Song song, bool autoPlay, TimeSpan? seekTo = null)
    {
        TeardownOutput();
        _releasedSong = null;
        _pendingPlaybackRestore = null;
        _current = null;

        var deck = new AudioDeck(song, _openSource(song.FilePath), _settings.SampleRateHz);
        try
        {
            if (seekTo is TimeSpan position && position > TimeSpan.Zero && position < deck.Duration)
                deck.Seek(position);
            BuildOutput(deck);
        }
        catch
        {
            deck.Dispose();
            TeardownOutput();
            throw;
        }

        _current = deck;
        if (autoPlay)
            _output!.Play();
    }

    public void Play()
    {
        if (_output == null)
            return;
        _pauseVersion++;
        if (_pauseGate != null)
            _pauseGate.IsOpen = true;
        _output.Play();
    }

    /// <summary>Silences the gate first, then pauses the device once its buffer has drained.</summary>
    public void Pause()
    {
        if (_output == null || _pauseGate == null)
            return;
        _pauseGate.IsOpen = false;
        _ = PauseAfterDrainAsync(++_pauseVersion);
    }

    public void Seek(TimeSpan position)
    {
        if (_mixer is not { } mixer)
            return;
        mixer.Seek(position);
        if (_armSong is { } song && !_armOpened)
            ScheduleArm(mixer, song, _armOverlap);
    }

    public void Stop()
    {
        TeardownOutput();
        _current = null;
        _releasedSong = null;
        _pendingPlaybackRestore = null;
    }

    /// <summary>Opens the next track off the UI thread shortly before the transition needs it.</summary>
    public void ArmNext(Song? song, bool overlap)
    {
        if (_mixer is not { } mixer || (ReferenceEquals(song, _armSong) && overlap == _armOverlap))
            return;

        _armSong = song;
        _armOverlap = overlap;
        _armOpened = false;
        Interlocked.Increment(ref _armVersion);
        mixer.Arm(null, false)?.Dispose();
        if (song != null)
            ScheduleArm(mixer, song, overlap);
    }

    private void ScheduleArm(CrossfadeMixer mixer, Song song, bool overlap)
    {
        int version = Interlocked.Increment(ref _armVersion);
        double wait = Duration.TotalSeconds - Position.TotalSeconds - _settings.CrossfadeSeconds - ArmLeadSeconds;
        PendingArm = OpenArmedDeckAsync(mixer, song, overlap, version, TimeSpan.FromSeconds(Math.Max(0, wait)));
    }

    public void ApplySettings(EngineSettings settings)
    {
        var previous = _settings;
        _settings = settings;
        _mixer?.SetCrossfade(settings.CrossfadeSeconds, settings.RampSeconds);

        // The pending open was timed for the old crossfade length; a longer fade needs the next file sooner.
        if (previous.CrossfadeSeconds != settings.CrossfadeSeconds && _mixer is { } liveMixer &&
            _armSong is { } pendingSong && !_armOpened)
            ScheduleArm(liveMixer, pendingSong, _armOverlap);

        bool rebuild = previous.Backend != settings.Backend || previous.SoftwareVolume != settings.SoftwareVolume ||
                       previous.SampleRateHz != settings.SampleRateHz || previous.Bits != settings.Bits;
        if (!rebuild || _current == null)
            return;

        var (song, playing, position, armSong, armOverlap) = (_current.Song, IsPlaying, Position, _armSong, _armOverlap);
        Load(song, playing, position);
        ArmNext(armSong, armOverlap);
    }

    /// <summary>
    /// Closes any open handle on the file so its tags can be rewritten, and keeps it closed until the
    /// matching <see cref="RestoreFile"/>: other writers' restores re-arm the next track, which used to reopen
    /// a file still being written.
    /// </summary>
    public MetadataAudioReleaseResult ReleaseFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return default;
        _heldFiles[path] = _heldFiles.GetValueOrDefault(path) + 1;
        var held = new MetadataAudioReleaseResult { HeldPath = path };

        if (_mixer is not { } mixer)
        {
            CloseDeckBeingOpened(path);
            return held;
        }

        if (!mixer.IsPlayingFile(path))
        {
            if (mixer.IsArmedWith(path) || (_armSong != null &&
                string.Equals(_armSong.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                ArmNext(null, false);
            CloseDeckBeingOpened(path);
            return held;
        }

        var result = new MetadataAudioReleaseResult
        {
            ReleasedPlayback = true,
            Position = Position,
            WasPlaying = IsPlaying,
            HeldPath = path
        };
        _releasedSong = CurrentSong;
        TeardownOutput();
        _current = null;
        CloseDeckBeingOpened(path);
        return result;
    }

    public void RestoreFile(MetadataAudioReleaseResult release)
    {
        if (release.HeldPath is { } path && _heldFiles.TryGetValue(path, out int holds))
        {
            if (holds <= 1)
                _heldFiles.Remove(path);
            else
                _heldFiles[path] = holds - 1;
        }

        // Playback comes back only when the last writer of the playing file is done; two overlapping writers
        // (an artwork batch and an Info edit) used to reopen it while the second was still writing.
        // Only a snapshot of the song that is released now counts; one from an earlier release would reload
        // a different song at the old position.
        if (release.ReleasedPlayback && _releasedSong != null &&
            string.Equals(release.HeldPath, _releasedSong.FilePath, StringComparison.OrdinalIgnoreCase))
            _pendingPlaybackRestore = release;
        if (_pendingPlaybackRestore is { } pending && _releasedSong is { } song &&
            (pending.HeldPath == null || !_heldFiles.ContainsKey(pending.HeldPath)))
        {
            _pendingPlaybackRestore = null;
            Load(song, pending.WasPlaying, pending.Position);
        }
        else if (_mixer is { } mixer && _armSong is { } armSong && !_armOpened)
            ScheduleArm(mixer, armSong, _armOverlap); // its open may have been skipped while the file was held
    }

    /// <summary>
    /// Background opens of this file can be in flight when a write starts. Wait for them (they never need the UI
    /// thread) and close the decks they produced, so the writer never meets an open handle.
    /// </summary>
    private void CloseDeckBeingOpened(string path)
    {
        foreach (var (open, openPath) in _opening.ToArray())
        {
            if (!string.Equals(openPath, path, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                open.Wait();
            }
            catch
            {
                // A failed open holds no handle.
            }
        }

        lock (_openLock)
        {
            foreach (var deck in _openedNotArmed.ToArray())
            {
                if (!string.Equals(deck.Song.FilePath, path, StringComparison.OrdinalIgnoreCase))
                    continue;
                deck.Dispose();
                _openedNotArmed.Remove(deck);
            }
        }
    }

    public void Dispose() => Stop();

    private void BuildOutput(AudioDeck deck)
    {
        int generation = ++_generation;
        var mixer = new CrossfadeMixer(PlaybackResampler.NormalizeOutputSampleRateHz(_settings.SampleRateHz));
        mixer.SetCrossfade(_settings.CrossfadeSeconds, _settings.RampSeconds);
        mixer.DeckStarted += d => Post(generation, () => OnDeckStarted(d));
        mixer.DeckFinished += DisposeOnContext;
        mixer.SetCurrent(deck);
        _mixer = mixer;

        ISampleProvider chain = mixer;
        _master = null;
        if (_settings.SoftwareVolume)
            chain = _master = new VolumeSampleProvider(mixer) { Volume = _volume };
        _pauseGate = new PauseGateSampleProvider(chain);

        var wave = PlaybackOutputBitsUtil.ApplyToIeeeFloatChain(_settings.Bits, new SampleToWaveProvider(_pauseGate));
        _output = CreateInitializedOutput(wave);
        _stoppedHandler = (_, e) => Post(generation, () => OnOutputStopped(e));
        _output.PlaybackStopped += _stoppedHandler;
        if (!_settings.SoftwareVolume)
            _output.Volume = _volume;
    }

    private IWavePlayer CreateInitializedOutput(IWaveProvider wave)
    {
        // _output is not set yet, so a device that fails Init must be disposed here or it leaks.
        var output = _createOutput(_settings.Backend);
        try
        {
            output.Init(wave);
            return output;
        }
        catch when (_settings.Backend == AudioOutputBackend.WasapiExclusive)
        {
            // Exclusive mode can reject the format at Init, after the device was created.
            output.Dispose();
            var shared = _createOutput(AudioOutputBackend.WasapiShared);
            try
            {
                shared.Init(wave);
                return shared;
            }
            catch
            {
                shared.Dispose();
                throw;
            }
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    private async Task PauseAfterDrainAsync(int version)
    {
        await Task.Delay(PauseDrainMs);
        if (version != _pauseVersion || _output is not { } output || _pauseGate is not { IsOpen: false })
            return;

        if (output is not WasapiOut)
        {
            output.Pause();
            return;
        }

        // WASAPI Pause() leaves the device running, which can buzz in exclusive mode. Stop() halts
        // it, and dropping the handler keeps that stop from reading as the track ending.
        output.PlaybackStopped -= _stoppedHandler;
        output.Stop();
        output.PlaybackStopped += _stoppedHandler;
    }

    private void TeardownOutput()
    {
        _generation++;
        _pauseVersion++;
        _pauseGate = null;
        Interlocked.Increment(ref _armVersion);
        _armSong = null;
        _armOverlap = false;
        _armOpened = false;

        if (_output != null)
        {
            if (_stoppedHandler != null)
                _output.PlaybackStopped -= _stoppedHandler;
            _output.Stop();
            _output.Dispose();
        }
        _output = null;
        _stoppedHandler = null;
        _master = null;

        if (_mixer != null)
        {
            foreach (var deck in _mixer.TakeAll())
                deck.Dispose();
        }
        _mixer = null;
    }

    private async Task OpenArmedDeckAsync(CrossfadeMixer mixer, Song song, bool overlap, int version, TimeSpan wait)
    {
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait);
            if (version != Volatile.Read(ref _armVersion))
                return;
        }

        // A file being written stays closed; RestoreFile schedules this again once the write is done.
        if (_heldFiles.ContainsKey(song.FilePath))
            return;

        int sampleRate = _settings.SampleRateHz;
        var open = Task.Run(() =>
        {
            var opened = new AudioDeck(song, _openSource(song.FilePath), sampleRate);
            // Checked under the lock so a ReleaseFile that disarmed meanwhile either sees this deck or makes
            // this check fail; either way the deck is closed before the writer touches the file.
            lock (_openLock)
            {
                if (version != Volatile.Read(ref _armVersion))
                {
                    opened.Dispose();
                    return null;
                }
                _openedNotArmed.Add(opened);
                return opened;
            }
        });
        // Every open is tracked (not just the latest), so a queue change mid-open can't hide one from ReleaseFile.
        _opening.RemoveAll(o => o.Open.IsCompleted);
        _opening.Add((open, song.FilePath));

        AudioDeck? deck;
        try
        {
            deck = await open;
        }
        catch (Exception)
        {
            // A bad next track surfaces when playback actually reaches it.
            return;
        }
        if (deck == null)
            return;

        lock (_openLock)
        {
            if (!_openedNotArmed.Remove(deck))
                return; // ReleaseFile already closed it
        }

        if (version != Volatile.Read(ref _armVersion))
        {
            deck.Dispose();
            return;
        }
        mixer.Arm(deck, overlap)?.Dispose();
        _armOpened = true;
    }

    private void OnDeckStarted(AudioDeck deck)
    {
        _current = deck;
        _armSong = null;
        _armOverlap = false;
        _armOpened = false;
        TrackStarted?.Invoke(deck.Song);
    }

    private void OnOutputStopped(StoppedEventArgs e)
    {
        if (e.Exception != null)
            Failed?.Invoke(e.Exception);
        else
            PlaybackEnded?.Invoke();
    }

    private void Post(int generation, Action action)
    {
        void Run()
        {
            if (generation == _generation)
                action();
        }

        if (_context != null)
            _context.Post(_ => Run(), null);
        else
            Run();
    }

    private void DisposeOnContext(AudioDeck deck)
    {
        if (_context != null)
            _context.Post(_ => deck.Dispose(), null);
        else
            deck.Dispose();
    }
}
