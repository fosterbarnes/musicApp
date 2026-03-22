using System;

namespace MusicApp;

public readonly struct MetadataAudioReleaseResult
{
    public bool ReleasedPlayback { get; init; }
    public TimeSpan Position { get; init; }
    public bool WasPlaying { get; init; }
}
