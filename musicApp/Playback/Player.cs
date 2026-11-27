using System;
using System.Collections.Generic;

namespace musicApp.Playback;

/// <summary>The playback API for the UI: applies repeat, shuffle, and play-count rules.</summary>
public sealed class Player : IDisposable
{
    private readonly AudioEngine _engine;
    private readonly double _previousEdgeSeconds;
    private RepeatMode _repeat = RepeatMode.Off;
    private bool _playCounted;

    public Player(AudioEngine engine, double previousEdgeSeconds, Random? random = null)
    {
        _engine = engine;
        _previousEdgeSeconds = previousEdgeSeconds;
        Queue = new PlaybackQueue(random);
        _engine.TrackStarted += OnTrackStarted;
        _engine.PlaybackEnded += OnPlaybackEnded;
        _engine.Failed += Fail;
    }

    public PlaybackQueue Queue { get; }
    public Song? CurrentTrack => _engine.CurrentSong;
    public bool IsPlaying => _engine.IsPlaying;
    public TimeSpan Position => _engine.Position;
    public TimeSpan Duration => _engine.Duration;

    public event Action<Song?>? TrackChanged;
    public event Action<Song>? TrackPlayed;
    public event Action<bool>? PlaybackStateChanged;
    public event Action? QueueChanged;
    public event Action<Exception>? Failed;

    public RepeatMode Repeat
    {
        get => _repeat;
        set
        {
            _repeat = value;
            ArmNext();
        }
    }

    public float Volume
    {
        get => _engine.Volume;
        set => _engine.Volume = value;
    }

    /// <summary>Plays a track. With a context it starts a new queue, otherwise it jumps within this one.</summary>
    public void Play(Song track, IReadOnlyList<Song>? context)
    {
        bool placed = context != null ? Queue.Start(context, track) : Queue.JumpTo(track);
        if (!placed)
            Queue.Start(new[] { track }, track);
        LoadCurrent(autoPlay: true, isNewEntry: true);
    }

    public void PlayPause()
    {
        if (CurrentTrack == null)
        {
            if (Queue.Current != null || Queue.Advance(_repeat) != null)
                LoadCurrent(autoPlay: true, isNewEntry: true);
            return;
        }

        if (IsPlaying)
        {
            _engine.Pause();
        }
        else
        {
            _engine.Play();
            CountPlay();
        }
        PlaybackStateChanged?.Invoke(IsPlaying);
    }

    public void Next()
    {
        bool wasPlaying = IsPlaying;
        if (Queue.Advance(_repeat) != null)
            LoadCurrent(wasPlaying, isNewEntry: true);
        else
            Stop(clearQueue: true);
    }

    public void Previous()
    {
        bool wasPlaying = IsPlaying;
        if (Position.TotalSeconds <= _previousEdgeSeconds && Queue.Rewind() != null)
            LoadCurrent(wasPlaying, isNewEntry: true);
        else if (Queue.Current != null)
            LoadCurrent(wasPlaying, isNewEntry: false);
    }

    public void Seek(TimeSpan position) => _engine.Seek(position);

    public void Stop(bool clearQueue)
    {
        _engine.Stop();
        if (clearQueue)
            Queue.Clear(offerUndo: true);
        TrackChanged?.Invoke(null);
        PlaybackStateChanged?.Invoke(false);
        QueueChanged?.Invoke();
    }

    public void SetShuffle(bool enabled)
    {
        Queue.SetShuffle(enabled);
        AfterQueueEdit();
    }

    public void PlayNext(Song track)
    {
        Queue.PlayNext(track);
        AfterQueueEdit();
    }

    public void AddToQueue(Song track)
    {
        Queue.Add(track);
        AfterQueueEdit();
    }

    /// <summary>Queues several tracks at once, keeping their list order.</summary>
    public void InjectTracks(IReadOnlyList<Song> tracks, bool playNext)
    {
        if (playNext && Queue.Current != null)
        {
            for (int i = tracks.Count - 1; i >= 0; i--)
                Queue.PlayNext(tracks[i]);
        }
        else
        {
            foreach (var track in tracks)
                Queue.Add(track);
        }
        AfterQueueEdit();
    }

    public void MoveInQueue(int fromView, int toView)
    {
        if (Queue.Move(fromView, toView))
            AfterQueueEdit();
    }

    public void RemoveFromQueue(IEnumerable<int> viewIndexes)
    {
        bool wasPlaying = IsPlaying;
        bool currentRemoved = false;
        // Highest first, so each removal leaves the lower indexes valid
        foreach (int viewIndex in viewIndexes.Distinct().OrderDescending())
            currentRemoved |= Queue.RemoveAt(viewIndex);
        if (!currentRemoved)
        {
            AfterQueueEdit();
            return;
        }

        if (Queue.Current != null)
            LoadCurrent(wasPlaying, isNewEntry: true);
        else
            Stop(clearQueue: true);
    }

    public bool SwapCurrentAndNext()
    {
        bool wasPlaying = IsPlaying;
        if (Queue.SwapCurrentAndNext() == null)
            return false;
        LoadCurrent(wasPlaying, isNewEntry: true);
        return true;
    }

    public bool UndoQueue()
    {
        if (!Queue.TryUndo())
            return false;
        AfterQueueEdit();
        return true;
    }

    /// <summary>
    /// Removes tracks from the queue. If the playing one goes, the next track loads (playing only if
    /// playback was), like removing it from the queue; the rest of the queue is kept.
    /// </summary>
    public void RemoveTracks(IReadOnlySet<string> paths)
    {
        bool wasPlaying = IsPlaying;
        bool currentRemoved = CurrentTrack != null && paths.Contains(CurrentTrack.FilePath);
        Queue.RemoveTracks(paths);
        if (!currentRemoved)
            AfterQueueEdit();
        else if (Queue.Current != null)
            LoadCurrent(wasPlaying, isNewEntry: true);
        else
            Stop(clearQueue: true);
    }

    public void ClearAll()
    {
        _engine.Stop();
        Queue.Clear(offerUndo: false);
        Queue.ClearUndo();
        TrackChanged?.Invoke(null);
        PlaybackStateChanged?.Invoke(false);
        QueueChanged?.Invoke();
    }

    public void ApplySettings(EngineSettings settings)
    {
        try
        {
            _engine.ApplySettings(settings);
            ArmNext();
            PlaybackStateChanged?.Invoke(IsPlaying);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    public MetadataAudioReleaseResult ReleaseFile(string path) => _engine.ReleaseFile(path);

    public void RestoreFile(MetadataAudioReleaseResult release)
    {
        try
        {
            _engine.RestoreFile(release);
            ArmNext();
            // Any restore can be the one that resumes playback (the last writer of the playing file).
            PlaybackStateChanged?.Invoke(IsPlaying);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    public void Dispose() => _engine.Dispose();

    private void LoadCurrent(bool autoPlay, bool isNewEntry)
    {
        if (Queue.Current is not { } track)
            return;

        if (isNewEntry)
            _playCounted = false;
        try
        {
            _engine.Load(track, autoPlay);
        }
        catch (Exception ex)
        {
            Fail(ex);
            return;
        }

        if (autoPlay)
            CountPlay();
        ArmNext();
        TrackChanged?.Invoke(track);
        PlaybackStateChanged?.Invoke(IsPlaying);
        QueueChanged?.Invoke();
    }

    private void OnTrackStarted(Song song)
    {
        if (_repeat == RepeatMode.One && ReferenceEquals(song, Queue.Current))
        {
            ArmNext();
            return;
        }

        // The queue can change between the audio thread starting a track and this callback.
        bool placed = ReferenceEquals(Queue.PeekNext(_repeat), song)
            ? Queue.Advance(_repeat) != null
            : Queue.JumpTo(song);
        if (!placed)
        {
            LoadCurrent(IsPlaying, isNewEntry: true);
            return;
        }

        _playCounted = false;
        CountPlay();
        ArmNext();
        TrackChanged?.Invoke(song);
        QueueChanged?.Invoke();
    }

    private void OnPlaybackEnded()
    {
        if (_repeat == RepeatMode.One && Queue.Current != null)
        {
            LoadCurrent(autoPlay: true, isNewEntry: false);
            return;
        }

        if (Queue.Advance(_repeat) != null)
            LoadCurrent(autoPlay: true, isNewEntry: true);
        else
            Stop(clearQueue: true);
    }

    private void CountPlay()
    {
        if (_playCounted || CurrentTrack is not { } track)
            return;
        _playCounted = true;
        track.MarkAsPlayed();
        TrackPlayed?.Invoke(track);
    }

    private void ArmNext()
    {
        if (CurrentTrack == null)
            return;
        if (_repeat == RepeatMode.One)
            _engine.ArmNext(Queue.Current, overlap: false);
        else
            _engine.ArmNext(Queue.PeekNext(_repeat), overlap: true);
    }

    private void AfterQueueEdit()
    {
        ArmNext();
        QueueChanged?.Invoke();
    }

    private void Fail(Exception ex)
    {
        _engine.Stop();
        TrackChanged?.Invoke(null);
        PlaybackStateChanged?.Invoke(false);
        QueueChanged?.Invoke();
        Failed?.Invoke(ex);
    }
}
