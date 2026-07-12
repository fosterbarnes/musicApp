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

    public static IWaveProvider ToOutputWaveProvider(AudioFileReader reader, int targetSampleRate)
    {
        targetSampleRate = NormalizeOutputSampleRateHz(targetSampleRate);
        var sp = ResampleIfNeeded(reader.ToSampleProvider(), reader.WaveFormat, targetSampleRate);
        return new SampleToWaveProvider(sp);
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
