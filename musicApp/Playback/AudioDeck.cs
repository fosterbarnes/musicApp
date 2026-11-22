using System;
using System.Threading;
using musicApp.Helpers;
using NAudio.Wave;

namespace musicApp.Playback;

public interface IAudioSource : ISampleProvider, IDisposable
{
    TimeSpan Position { get; set; }
    TimeSpan Duration { get; }
}

public sealed class FileAudioSource(string path) : IAudioSource
{
    private readonly AudioFileReader _reader = new(path);

    public WaveFormat WaveFormat => _reader.WaveFormat;
    public TimeSpan Duration => _reader.TotalTime;

    public TimeSpan Position
    {
        get => _reader.CurrentTime;
        set => _reader.CurrentTime = value;
    }

    public int Read(float[] buffer, int offset, int count) => _reader.Read(buffer, offset, count);
    public void Dispose() => _reader.Dispose();
}

/// <summary>One open track, converted to stereo float at the output sample rate.</summary>
public sealed class AudioDeck : IDisposable
{
    private readonly IAudioSource _source;
    private readonly int _sampleRate;
    private ISampleProvider _chain;
    private long _originFrames;
    private long _renderedFrames;

    public AudioDeck(Song song, IAudioSource source, int sampleRate)
    {
        Song = song;
        _source = source;
        Duration = source.Duration; // Read once: the UI can ask after the deck is disposed.
        _sampleRate = PlaybackResampler.NormalizeOutputSampleRateHz(sampleRate);
        _chain = BuildChain();
    }

    public Song Song { get; }
    public TimeSpan Duration { get; }
    public TimeSpan Position => TimeSpan.FromSeconds((double)PositionFrames / _sampleRate);
    public long TotalFrames => (long)(Duration.TotalSeconds * _sampleRate);
    public long RemainingFrames => Math.Max(0, TotalFrames - PositionFrames);

    private long PositionFrames => Volatile.Read(ref _originFrames) + Volatile.Read(ref _renderedFrames);

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _chain.Read(buffer, offset, count);
        Volatile.Write(ref _renderedFrames, _renderedFrames + read / 2);
        return read;
    }

    public void Seek(TimeSpan position)
    {
        var target = position < TimeSpan.Zero ? TimeSpan.Zero : position > Duration ? Duration : position;
        _source.Position = target;
        _chain = BuildChain(); // The resampler holds audio from the old position.
        Volatile.Write(ref _originFrames, (long)(target.TotalSeconds * _sampleRate));
        Volatile.Write(ref _renderedFrames, 0);
    }

    public void Dispose() => _source.Dispose();

    private ISampleProvider BuildChain()
    {
        ISampleProvider stereo = _source.WaveFormat.Channels == 2 ? _source : new StereoSampleProvider(_source);
        return PlaybackResampler.ResampleIfNeeded(stereo, stereo.WaveFormat, _sampleRate);
    }
}

/// <summary>Mono is duplicated, 5.1 is downmixed, and other layouts use their first two channels.</summary>
internal sealed class StereoSampleProvider : ISampleProvider
{
    private const float CenterGain = 0.7071f;
    private const float SurroundNormalize = 1f / (1f + CenterGain + CenterGain);

    private readonly ISampleProvider _source;
    private readonly int _channels;
    private float[] _buffer = Array.Empty<float>();

    public StereoSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        int frames = count / 2;
        int needed = frames * _channels;
        if (_buffer.Length < needed)
            _buffer = new float[needed];

        int framesRead = _source.Read(_buffer, 0, needed) / _channels;
        for (int f = 0; f < framesRead; f++)
        {
            int s = f * _channels;
            (float left, float right) = _channels switch
            {
                1 => (_buffer[s], _buffer[s]),
                6 => ((_buffer[s] + CenterGain * (_buffer[s + 2] + _buffer[s + 4])) * SurroundNormalize,
                    (_buffer[s + 1] + CenterGain * (_buffer[s + 2] + _buffer[s + 5])) * SurroundNormalize),
                _ => (_buffer[s], _buffer[s + 1])
            };
            buffer[offset + f * 2] = left;
            buffer[offset + f * 2 + 1] = right;
        }
        return framesRead * 2;
    }
}
