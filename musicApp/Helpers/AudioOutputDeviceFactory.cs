using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace musicApp.Helpers
{
    public static class AudioOutputDeviceFactory
    {
        public static IWavePlayer Create(AudioOutputBackend backend)
        {
            try
            {
                return backend switch
                {
                    AudioOutputBackend.WasapiExclusive => new WasapiOut(AudioClientShareMode.Exclusive, 200),
                    AudioOutputBackend.DirectSound => new DirectSoundOut(),
                    AudioOutputBackend.WaveOut => new WaveOutEvent(),
                    _ => new WasapiOut()
                };
            }
            catch (Exception)
            {
                // Exclusive can fail (device/format); shared WASAPI is the fallback.
                if (backend == AudioOutputBackend.WasapiExclusive)
                    return new WasapiOut();
                throw;
            }
        }
    }
}
