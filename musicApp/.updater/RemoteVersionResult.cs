namespace musicApp.Updater;

internal readonly struct RemoteVersionResult
{
    public Version? LatestVersion { get; init; }
    public string? LatestTagName { get; init; }
    public string? LatestReleaseVersionTag { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public string? Error { get; init; }
}
