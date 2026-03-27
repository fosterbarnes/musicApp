namespace musicApp.Helpers;

public enum AlbumArtFetchSource
{
    TagMbidCoverArtArchive,
    MusicBrainzSearchCoverArtArchive,
    FruitAppSearch,
    DeezerSearch
}

public sealed class AlbumArtFetchBytesResult
{
    public bool Ok { get; init; }
    public byte[]? ImageBytes { get; init; }
    public string? MusicBrainzReleaseId { get; init; }
    public AlbumArtFetchSource? Source { get; init; }
    public string? ErrorMessage { get; init; }

    public static AlbumArtFetchBytesResult Fail(string message) =>
        new() { Ok = false, ErrorMessage = message };

    public static AlbumArtFetchBytesResult Success(byte[] imageBytes, string? musicBrainzReleaseId, AlbumArtFetchSource source) =>
        new()
        {
            Ok = true,
            ImageBytes = imageBytes,
            MusicBrainzReleaseId = musicBrainzReleaseId,
            Source = source
        };
}

public readonly struct AlbumArtBatchItemResult
{
    public string FilePath { get; init; }
    public bool Ok { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class AlbumArtBatchProgress
{
    public int Done { get; init; }
    public int Total { get; init; }
    public string? CurrentPath { get; init; }
    public string? Phase { get; init; }
}

/// <summary>
/// Settings UI progress for Scan missing album art.
/// Total &lt; 0: progress bar indeterminate. Otherwise Done/Total fill the bar.
/// </summary>
public readonly record struct RemoteAlbumArtScanUiProgress(int Done, int Total, string Message);
