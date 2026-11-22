using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace musicApp.Helpers;

public enum AudioOutputBackend
{
    WasapiShared,
    WasapiExclusive,
    DirectSound,
    WaveOut
}

public static class AudioOutputDeviceFactory
{
    public static IWavePlayer Create(AudioOutputBackend backend)
    {
        try
        {
            return backend switch
            {
                AudioOutputBackend.WasapiExclusive => new WasapiOut(AudioClientShareMode.Exclusive, 50),
                AudioOutputBackend.DirectSound => new DirectSoundOut(100),
                AudioOutputBackend.WaveOut => new WaveOutEvent() { DesiredLatency = 100 },
                _ => new WasapiOut(AudioClientShareMode.Shared, 50)
            };
        }
        catch (Exception)
        {
            // Exclusive can fail (device/format); shared WASAPI is the fallback.
            if (backend == AudioOutputBackend.WasapiExclusive)
                return new WasapiOut(AudioClientShareMode.Shared, 50);
            throw;
        }
    }
}

public static class PlaybackResampler
{
    public static readonly int[] AllowedOutputSampleRates = { 44100, 48000, 88200, 96000 };

    public const int DefaultOutputSampleRateHz = 48000;

    public static int NormalizeOutputSampleRateHz(int hz)
    {
        return hz switch
        {
            44100 or 48000 or 88200 or 96000 => hz,
            _ => DefaultOutputSampleRateHz,
        };
    }

    public static ISampleProvider ResampleIfNeeded(ISampleProvider source, WaveFormat sourceFormat, int targetSampleRate)
    {
        targetSampleRate = NormalizeOutputSampleRateHz(targetSampleRate);
        if (sourceFormat.SampleRate == targetSampleRate)
            return source;
        return new WdlResamplingSampleProvider(source, targetSampleRate);
    }
}

// Fades out on pause and back in on resume so neither clicks. While closed it reads nothing,
// so the track position holds and whatever the device still has buffered is silence.
public sealed class PauseGateSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float _step;
    private float _gain = 1f;
    private volatile bool _open = true;

    public PauseGateSampleProvider(ISampleProvider source, double fadeMs = 15)
    {
        _source = source;
        _step = 1f / Math.Max(1, (int)(source.WaveFormat.SampleRate * fadeMs / 1000));
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public bool IsOpen
    {
        get => _open;
        set => _open = value;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        bool open = _open;
        if (!open && _gain <= 0f)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }

        int read = _source.Read(buffer, offset, count);
        if (open && _gain >= 1f)
            return read;

        int channels = WaveFormat.Channels;
        for (int i = 0; i < read; i += channels)
        {
            _gain = open ? Math.Min(1f, _gain + _step) : Math.Max(0f, _gain - _step);
            for (int c = 0; c < channels && i + c < read; c++)
                buffer[offset + i + c] *= _gain;
        }
        return read;
    }
}

public enum PlaybackOutputBits
{
    Pcm16,
    Pcm24,
    IeeeFloat,
}

public static class PlaybackOutputBitsUtil
{
    public const PlaybackOutputBits Default = PlaybackOutputBits.Pcm16;

    public static PlaybackOutputBits Normalize(PlaybackOutputBits v)
    {
        return Enum.IsDefined(typeof(PlaybackOutputBits), v) ? v : Default;
    }

    public static IWaveProvider ApplyToIeeeFloatChain(PlaybackOutputBits bits, IWaveProvider ieeeFloatSource)
    {
        bits = Normalize(bits);
        return bits switch
        {
            PlaybackOutputBits.IeeeFloat => ieeeFloatSource,
            PlaybackOutputBits.Pcm16 => new WaveFloatTo16Provider(ieeeFloatSource),
            PlaybackOutputBits.Pcm24 => ToPcm24(ieeeFloatSource),
            _ => new WaveFloatTo16Provider(ieeeFloatSource),
        };
    }

    private static IWaveProvider ToPcm24(IWaveProvider ieeeFloatSource)
    {
        return new SampleToWaveProvider24(ieeeFloatSource.ToSampleProvider());
    }
}
