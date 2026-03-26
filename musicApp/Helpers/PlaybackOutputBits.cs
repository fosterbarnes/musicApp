using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace musicApp.Helpers
{
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
}
