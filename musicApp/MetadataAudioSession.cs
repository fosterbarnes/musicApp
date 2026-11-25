using System;

namespace musicApp;

public readonly struct MetadataAudioReleaseResult
{
    public bool ReleasedPlayback { get; init; }
    public TimeSpan Position { get; init; }
    public bool WasPlaying { get; init; }

    /// <summary>File the engine must not open until this release is restored (it is being written).</summary>
    public string? HeldPath { get; init; }
}
