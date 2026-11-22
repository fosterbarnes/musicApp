using System;
using System.Collections.Generic;
using NAudio.Wave;

namespace musicApp.Playback;

/// <summary>
/// Plays the current deck and moves to the armed deck. Fades are calculated per frame on the audio
/// thread. Events fire after the lock is released, still on the audio thread.
/// </summary>
public sealed class CrossfadeMixer : ISampleProvider
{
    private readonly object _gate = new();
    private readonly List<(AudioDeck Deck, bool Started)> _pending = new();
    private AudioDeck? _current;
    private AudioDeck? _tail;
    private AudioDeck? _armed;
    private bool _armedOverlaps;
    private long _overlapFrames;
    private long _rampFrames;
    private long _rampLength;
    private long _rampPosition;
    private float[] _tailBuffer = Array.Empty<float>();

    public CrossfadeMixer(int sampleRate) =>
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);

    public WaveFormat WaveFormat { get; }

    public event Action<AudioDeck>? DeckStarted;
    public event Action<AudioDeck>? DeckFinished;

    public void SetCrossfade(double overlapSeconds, double rampSeconds)
    {
        lock (_gate)
        {
            _overlapFrames = (long)(Math.Max(0, overlapSeconds) * WaveFormat.SampleRate);
            _rampFrames = (long)(Math.Clamp(rampSeconds, 0, Math.Max(0, overlapSeconds)) * WaveFormat.SampleRate);
        }
    }

    public void SetCurrent(AudioDeck deck)
    {
        lock (_gate)
            _current = deck;
    }

    /// <summary>Sets the next deck and returns the one it replaced.</summary>
    public AudioDeck? Arm(AudioDeck? deck, bool overlaps)
    {
        lock (_gate)
        {
            var previous = _armed;
            _armed = deck;
            _armedOverlaps = overlaps;
            return previous;
        }
    }

    public void Seek(TimeSpan position)
    {
        lock (_gate)
            _current?.Seek(position);
    }

    public bool IsPlayingFile(string path)
    {
        lock (_gate)
            return IsFile(_current, path) || IsFile(_tail, path);
    }

    public bool IsArmedWith(string path)
    {
        lock (_gate)
            return IsFile(_armed, path);
    }

    public List<AudioDeck> TakeAll()
    {
        lock (_gate)
        {
            var decks = new List<AudioDeck>(3);
            foreach (var deck in new[] { _current, _tail, _armed })
            {
                if (deck != null)
                    decks.Add(deck);
            }
            _current = _tail = _armed = null;
            return decks;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int written = 0;
        (AudioDeck Deck, bool Started)[]? events = null;

        lock (_gate)
        {
            while (written < count && _current != null)
            {
                int want = count - written;
                if (_tail == null && _armed != null && _armedOverlaps && _overlapFrames > 0)
                {
                    // Short tracks only give up half their length to the fade.
                    long overlap = Math.Min(_overlapFrames, _current.TotalFrames / 2);
                    long framesUntil = _current.RemainingFrames - overlap;
                    if (framesUntil <= 0)
                        Transition(fade: true);
                    else
                        want = (int)Math.Min(want, framesUntil * 2);
                }

                int got = Render(buffer, offset + written, want);
                if (got > 0)
                {
                    written += got;
                    continue;
                }

                FinishTail();
                if (_armed != null)
                {
                    Transition(fade: false);
                }
                else
                {
                    _pending.Add((_current, false));
                    _current = null;
                }
            }

            if (_pending.Count > 0)
            {
                events = _pending.ToArray();
                _pending.Clear();
            }
        }

        if (events != null)
        {
            foreach (var (deck, started) in events)
                (started ? DeckStarted : DeckFinished)?.Invoke(deck);
        }
        return written;
    }

    private void Transition(bool fade)
    {
        var outgoing = _current!;
        _current = _armed;
        _armed = null;

        if (fade && _rampFrames > 0)
        {
            _tail = outgoing;
            _rampLength = Math.Max(1, Math.Min(_rampFrames, outgoing.RemainingFrames));
            _rampPosition = 0;
        }
        else
        {
            _pending.Add((outgoing, false));
        }
        _pending.Add((_current!, true));
    }

    private int Render(float[] buffer, int offset, int count)
    {
        int got = ReadFully(_current!, buffer, offset, count);
        if (_tail == null || got == 0)
            return got;

        if (_tailBuffer.Length < got)
            _tailBuffer = new float[got];
        int tailGot = ReadFully(_tail, _tailBuffer, 0, got);
        Array.Clear(_tailBuffer, tailGot, got - tailGot);

        for (int frame = 0; frame < got / 2; frame++)
        {
            double t = Math.Min(1d, (double)(_rampPosition + frame) / _rampLength);
            float incoming = (float)Math.Sin(t * Math.PI / 2);
            float outgoing = (float)Math.Cos(t * Math.PI / 2);
            for (int channel = 0; channel < 2; channel++)
            {
                int i = frame * 2 + channel;
                buffer[offset + i] = Math.Clamp(
                    buffer[offset + i] * incoming + _tailBuffer[i] * outgoing, -1f, 1f);
            }
        }

        _rampPosition += got / 2;
        if (_rampPosition >= _rampLength)
            FinishTail();
        return got;
    }

    private void FinishTail()
    {
        if (_tail == null)
            return;
        _pending.Add((_tail, false));
        _tail = null;
    }

    private static int ReadFully(AudioDeck deck, float[] buffer, int offset, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read = deck.Read(buffer, offset + total, count - total);
            if (read <= 0)
                break;
            total += read;
        }
        return total;
    }

    private static bool IsFile(AudioDeck? deck, string path) =>
        deck != null && string.Equals(deck.Song.FilePath, path, StringComparison.OrdinalIgnoreCase);
}
