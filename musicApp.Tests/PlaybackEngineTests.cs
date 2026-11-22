using musicApp;
using musicApp.Helpers;
using musicApp.Playback;
using NAudio.Wave;
using Xunit;

namespace musicApp.Tests;

public class PlaybackEngineTests
{
    private const int Rate = 44100;

    private sealed class ConstantSource(double seconds, float value, int rate = Rate, int channels = 2) : IAudioSource
    {
        private long _sample;
        private readonly long _total = (long)(seconds * rate) * channels;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(rate, channels);
        public TimeSpan Duration => TimeSpan.FromSeconds(seconds);

        public TimeSpan Position
        {
            get => TimeSpan.FromSeconds((double)_sample / channels / rate);
            set => _sample = (long)(value.TotalSeconds * rate) * channels;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int n = (int)Math.Min(count, _total - _sample);
            Array.Fill(buffer, value, offset, n);
            _sample += n;
            return n;
        }

        public void Dispose() { }
    }

    private sealed class FakeOutput : IWavePlayer
    {
        private IWaveProvider? _wave;

        public float Volume { get; set; } = 1f;
        public PlaybackState PlaybackState { get; private set; }
        public WaveFormat OutputWaveFormat => _wave!.WaveFormat;
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;

        public void Init(IWaveProvider waveProvider) => _wave = waveProvider;
        public void Play() => PlaybackState = PlaybackState.Playing;
        public void Pause() => PlaybackState = PlaybackState.Paused;
        public void Stop() => PlaybackState = PlaybackState.Stopped;
        public void Dispose() { }

        public void RaiseStopped() => PlaybackStopped?.Invoke(this, new StoppedEventArgs());

        public void Render(double seconds)
        {
            var format = _wave!.WaveFormat;
            int left = (int)(seconds * format.AverageBytesPerSecond);
            var buffer = new byte[format.BlockAlign * 1024];
            while (left > 0)
            {
                int read = _wave.Read(buffer, 0, Math.Min(buffer.Length, left - left % format.BlockAlign));
                if (read == 0)
                {
                    PlaybackState = PlaybackState.Stopped;
                    RaiseStopped();
                    return;
                }
                left -= read;
                if (left < format.BlockAlign)
                    break;
            }
        }
    }

    private sealed class Rig
    {
        public readonly List<FakeOutput> Outputs = new();
        public readonly AudioEngine Engine;
        public readonly Player Player;
        public readonly List<Song> Songs;

        public Rig(int crossfadeSeconds, double rampSeconds, double trackSeconds = 3)
        {
            Songs = "abc".Select(c => new Song { Title = c.ToString(), FilePath = $@"C:\music\{c}.wav" }).ToList();
            Engine = new AudioEngine(
                _ =>
                {
                    var output = new FakeOutput();
                    Outputs.Add(output);
                    return output;
                },
                _ => new ConstantSource(trackSeconds, 0.25f),
                context: null);
            Engine.ApplySettings(new EngineSettings(AudioOutputBackend.WasapiShared, true, Rate,
                PlaybackOutputBits.IeeeFloat, crossfadeSeconds, rampSeconds));
            Player = new Player(Engine, previousEdgeSeconds: 2);
        }

        public FakeOutput Output => Outputs[^1];

        public async Task PlayAsync(int index = 0)
        {
            Player.Play(Songs[index], Songs);
            await Engine.PendingArm;
        }
    }

    private static float[] RenderAll(CrossfadeMixer mixer)
    {
        var all = new List<float>();
        var buffer = new float[4096];
        int read;
        while ((read = mixer.Read(buffer, 0, buffer.Length)) > 0)
            all.AddRange(buffer.Take(read));
        return all.ToArray();
    }

    private static CrossfadeMixer ArmedMixer(IAudioSource first, IAudioSource second, double overlap, double ramp)
    {
        var mixer = new CrossfadeMixer(Rate);
        mixer.SetCrossfade(overlap, ramp);
        mixer.SetCurrent(new AudioDeck(new Song { FilePath = "a" }, first, Rate));
        mixer.Arm(new AudioDeck(new Song { FilePath = "b" }, second, Rate), overlaps: true);
        return mixer;
    }

    [Fact]
    public void OverlapLengthAndEqualPowerGainAreSampleAccurate()
    {
        var samples = RenderAll(ArmedMixer(new ConstantSource(2, 1f), new ConstantSource(2, 0f), 1, 1));

        Assert.Equal(3 * Rate * 2, samples.Length);
        Assert.Equal(1f, samples[(Rate - 1) * 2], 3);
        Assert.Equal(MathF.Sqrt(0.5f), samples[(int)(Rate * 1.5) * 2], 2);
        Assert.Equal(0f, samples[2 * Rate * 2 + 2], 3);
    }

    [Fact]
    public void MixedRatesAndMonoCrossfade()
    {
        var samples = RenderAll(ArmedMixer(
            new ConstantSource(2, 0.5f), new ConstantSource(2, 0.5f, rate: 48000, channels: 1), 1, 1));

        Assert.InRange(samples.Length, (int)(2.9 * Rate * 2), (int)(3.1 * Rate * 2));
    }

    [Fact]
    public void OutputNeverExceedsFullScale()
    {
        var samples = RenderAll(ArmedMixer(new ConstantSource(2, 1f), new ConstantSource(2, 1f), 1, 1));

        Assert.All(samples, s => Assert.InRange(s, -1f, 1f));
    }

    [Fact]
    public async Task StaleStopFromOldOutputIsIgnored()
    {
        var rig = new Rig(0, 0);
        await rig.PlayAsync();
        var old = rig.Output;
        rig.Player.Next();

        old.RaiseStopped();

        Assert.Equal("b", rig.Player.CurrentTrack?.Title);
    }

    [Fact]
    public async Task NaturalEndAdvancesExactlyOnce()
    {
        var rig = new Rig(0, 0, trackSeconds: 1);
        await rig.PlayAsync(1);

        rig.Output.Render(1.5);
        await rig.Engine.PendingArm;
        Assert.Equal("c", rig.Player.CurrentTrack?.Title);
        Assert.Equal(1, rig.Songs[1].PlayCount);
        Assert.Equal(1, rig.Songs[2].PlayCount);

        rig.Output.Render(1);
        Assert.Null(rig.Player.CurrentTrack);
    }

    [Fact]
    public async Task SkipAndSeekDuringFadeActOnTheNewTrack()
    {
        var rig = new Rig(1, 1);
        await rig.PlayAsync();
        rig.Output.Render(2.25);
        Assert.Equal("b", rig.Player.CurrentTrack?.Title);

        rig.Player.Seek(TimeSpan.FromSeconds(0.5));
        Assert.Equal(0.5, rig.Player.Position.TotalSeconds, 1);

        await rig.Engine.PendingArm;
        rig.Player.Next();
        Assert.Equal("c", rig.Player.CurrentTrack?.Title);
    }

    [Fact]
    public async Task RepeatOneDoesNotAdvance()
    {
        var rig = new Rig(1, 1);
        rig.Player.Repeat = RepeatMode.One;
        await rig.PlayAsync();

        rig.Output.Render(3.5);
        await rig.Engine.PendingArm;

        Assert.Equal("a", rig.Player.CurrentTrack?.Title);
        Assert.Equal(1, rig.Songs[0].PlayCount);
    }

    [Fact]
    public async Task RestartAndPausedSkipDoNotCountPlays()
    {
        var rig = new Rig(0, 0);
        await rig.PlayAsync();
        rig.Player.Previous();
        rig.Player.PlayPause();
        rig.Player.Next();

        Assert.Equal(1, rig.Songs[0].PlayCount);
        Assert.Equal(0, rig.Songs[1].PlayCount);
    }
}
