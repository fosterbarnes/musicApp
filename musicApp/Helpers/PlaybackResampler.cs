using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace musicApp.Helpers
{
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
}
