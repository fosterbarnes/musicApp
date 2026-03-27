using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ATL;
using MetaBrainz.MusicBrainz;
using MetaBrainz.MusicBrainz.CoverArt;
using MetaBrainz.MusicBrainz.Interfaces.Entities;
using MetaBrainz.MusicBrainz.Interfaces.Searches;

using musicApp;

namespace musicApp.Helpers;

public static class AlbumArtRemoteFetcher
{
    private const string MbContactUrl = "https://github.com/fosterbarnes/musicApp";

    private static Query CreateQuery()
    {
        var ver = AppReleaseVersion.ReadLabel();
        return new Query("musicApp", ver, MbContactUrl);
    }

    private static CoverArt CreateCoverArt()
    {
        var ver = AppReleaseVersion.ReadLabel();
        return new CoverArt("musicApp", ver, MbContactUrl);
    }

    private static readonly string[] ReleaseIdTagKeys =
    {
        "MUSICBRAINZ_RELEASEID",
        "MusicBrainz Release Id"
    };

    public static string? TryReadMusicBrainzReleaseIdFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        try
        {
            var t = new Track(filePath);
            foreach (var key in ReleaseIdTagKeys)
            {
                if (TryGetAdditional(t, key, out var raw) &&
                    !string.IsNullOrWhiteSpace(raw) &&
                    Guid.TryParse(raw.AsSpan().Trim(), out var g))
                    return g.ToString();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static bool TryGetAdditional(Track atl, string key, out string? value)
    {
        value = null;
        var f = atl.AdditionalFields;
        if (f == null)
            return false;

        if (f is IDictionary<string, string> gen)
            return gen.TryGetValue(key, out value);

        if (f is System.Collections.IDictionary legacy)
        {
            if (!legacy.Contains(key))
                return false;
            value = legacy[key]?.ToString();
            return true;
        }

        return false;
    }

    private static string LuceneToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        var s = raw.Trim();
        return "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static string PrimaryArtistForSearch(string? albumArtist, string? trackArtist)
    {
        if (!string.IsNullOrWhiteSpace(albumArtist))
            return albumArtist.Trim();
        return (trackArtist ?? "").Trim();
    }

    private static bool IsNonSpecificAlbumArtist(string? albumArtist)
    {
        if (string.IsNullOrWhiteSpace(albumArtist))
            return true;
        var t = albumArtist.Trim();
        return t.Equals("Various Artists", StringComparison.OrdinalIgnoreCase)
               || t.Equals("Various", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Prefer a real performing artist for fruitApp/Deezer search when the album artist is empty or a compilation placeholder.</summary>
    public static string EffectiveFruitAppSearchArtist(string? albumArtist, string? trackArtist)
    {
        if (IsNonSpecificAlbumArtist(albumArtist))
            return (trackArtist ?? "").Trim();
        return (albumArtist ?? "").Trim();
    }

    public static HttpClient CreateFruitAppSearchHttpClient()
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            $"musicApp/{AppReleaseVersion.ReadLabel()} ({MbContactUrl})");
        return http;
    }

    private static async Task<Guid?> TryResolveReleaseMbidAsync(
        Query query,
        string album,
        string albumArtist,
        string trackArtist,
        int? tagYear,
        CancellationToken cancellationToken)
    {
        var artist = PrimaryArtistForSearch(albumArtist, trackArtist);
        if (string.IsNullOrWhiteSpace(album) || string.IsNullOrWhiteSpace(artist))
            return null;

        var q = $"release:{LuceneToken(album)} AND artist:{LuceneToken(artist)}";
        ISearchResults<ISearchResult<IRelease>>? results;
        try
        {
            results = await query.FindReleasesAsync(q, limit: 10, offset: null, simple: false, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            var simple = $"{artist} {album}";
            try
            {
                results = await query
                    .FindReleasesAsync(simple, limit: 10, offset: null, simple: true, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        if (results?.Results == null || results.Results.Count == 0)
            return null;

        var ordered = results.Results.OrderByDescending(r => r.Score).ToList();
        if (tagYear is > 0 and <= 9999)
        {
            foreach (var sr in ordered)
            {
                var rel = sr.Item;
                if (rel == null)
                    continue;
                var y = rel.Date?.Year;
                if (y == tagYear)
                    return rel.Id;
            }
        }

        return ordered[0].Item?.Id;
    }

    private static async Task<byte[]?> TryFetchCoverArtFrontAsync(
        CoverArt client,
        Guid releaseMbid,
        CancellationToken cancellationToken)
    {
        try
        {
            using var img = await client.FetchFrontAsync(releaseMbid, CoverArtImageSize.Original, cancellationToken)
                .ConfigureAwait(false);
            await using var ms = new MemoryStream();
            await img.Data.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            var bytes = ms.ToArray();
            return bytes.Length > 0 ? bytes : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>fruitApp storefront (Apple Search API) artwork only; same path as <see cref="FetchCoverBytesAsync"/>.</summary>
    public static async Task<byte[]?> TryFetchFruitAppStorefrontCoverBytesAsync(
        string album,
        string? albumArtist,
        string? trackArtist,
        CancellationToken cancellationToken = default)
    {
        using var http = CreateFruitAppSearchHttpClient();
        return await TryFetchFruitAppStorefrontCoverBytesAsync(http, album, albumArtist, trackArtist, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<byte[]?> TryFetchFruitAppStorefrontCoverBytesAsync(
        HttpClient http,
        string album,
        string? albumArtist,
        string? trackArtist,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(album))
            return null;

        var artist = EffectiveFruitAppSearchArtist(albumArtist, trackArtist);
        return await StorefrontAlbumArtFallback.TryDownloadFromFruitAppStorefrontAsync(http, artist, album.Trim(), cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<AlbumArtFetchBytesResult> FetchCoverBytesAsync(
        string filePath,
        string album,
        string albumArtist,
        string trackArtist,
        int tagYear,
        IProgress<string>? phaseProgress,
        CancellationToken cancellationToken = default,
        HttpClient? sharedHttpForStorefront = null)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return AlbumArtFetchBytesResult.Fail("File not found.");

        int? yearHint = tagYear > 0 ? tagYear : null;

        phaseProgress?.Report("Checking tags for MusicBrainz release id…");
        var fromTag = TryReadMusicBrainzReleaseIdFromFile(filePath);
        if (fromTag != null && Guid.TryParse(fromTag, out var tagMbid))
        {
            phaseProgress?.Report("Downloading from Cover Art Archive…");
            using var caTag = CreateCoverArt();
            var fromCaa = await TryFetchCoverArtFrontAsync(caTag, tagMbid, cancellationToken).ConfigureAwait(false);
            if (fromCaa != null)
                return AlbumArtFetchBytesResult.Success(fromCaa, fromTag, AlbumArtFetchSource.TagMbidCoverArtArchive);
        }

        phaseProgress?.Report("Searching MusicBrainz…");
        using var query = CreateQuery();
        var mbid = await TryResolveReleaseMbidAsync(query, album, albumArtist, trackArtist, yearHint, cancellationToken)
            .ConfigureAwait(false);
        if (mbid is Guid g && g != Guid.Empty)
        {
            phaseProgress?.Report("Downloading from Cover Art Archive…");
            using var ca = CreateCoverArt();
            var bytes = await TryFetchCoverArtFrontAsync(ca, g, cancellationToken).ConfigureAwait(false);
            if (bytes != null)
                return AlbumArtFetchBytesResult.Success(bytes, g.ToString(),
                    AlbumArtFetchSource.MusicBrainzSearchCoverArtArchive);
        }

        phaseProgress?.Report("Trying fruitApp (fallback)…");
        var artist = EffectiveFruitAppSearchArtist(albumArtist, trackArtist);
        if (sharedHttpForStorefront != null)
        {
            var fruit = await StorefrontAlbumArtFallback.TryDownloadFromFruitAppStorefrontAsync(
                    sharedHttpForStorefront,
                    artist,
                    album,
                    cancellationToken)
                .ConfigureAwait(false);
            if (fruit is { Length: > 0 })
                return AlbumArtFetchBytesResult.Success(fruit, null, AlbumArtFetchSource.FruitAppSearch);

            phaseProgress?.Report("Trying Deezer (fallback)…");
            var dz = await StorefrontAlbumArtFallback.TryDownloadFromDeezerAsync(
                    sharedHttpForStorefront,
                    artist,
                    album,
                    cancellationToken)
                .ConfigureAwait(false);
            if (dz is { Length: > 0 })
                return AlbumArtFetchBytesResult.Success(dz, null, AlbumArtFetchSource.DeezerSearch);
        }
        else
        {
            using var http = CreateFruitAppSearchHttpClient();
            var fruit = await StorefrontAlbumArtFallback.TryDownloadFromFruitAppStorefrontAsync(http, artist, album, cancellationToken)
                .ConfigureAwait(false);
            if (fruit is { Length: > 0 })
                return AlbumArtFetchBytesResult.Success(fruit, null, AlbumArtFetchSource.FruitAppSearch);

            phaseProgress?.Report("Trying Deezer (fallback)…");
            var dz = await StorefrontAlbumArtFallback.TryDownloadFromDeezerAsync(http, artist, album, cancellationToken)
                .ConfigureAwait(false);
            if (dz is { Length: > 0 })
                return AlbumArtFetchBytesResult.Success(dz, null, AlbumArtFetchSource.DeezerSearch);
        }

        return AlbumArtFetchBytesResult.Fail(
            "No artwork found (MusicBrainz / Cover Art Archive / storefront fallbacks).");
    }

    public static async Task<IReadOnlyList<AlbumArtBatchItemResult>> FetchAndEmbedForFilesAsync(
        IReadOnlyList<string> filePaths,
        IProgress<AlbumArtBatchProgress>? batchProgress,
        Func<string, MetadataAudioReleaseResult> releasePlaybackForPath,
        Action<MetadataAudioReleaseResult> restorePlayback,
        CancellationToken cancellationToken = default)
    {
        var list = new List<AlbumArtBatchItemResult>();
        var total = filePaths.Count;
        for (var i = 0; i < filePaths.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = filePaths[i];
            batchProgress?.Report(new AlbumArtBatchProgress
            {
                Done = i,
                Total = total,
                CurrentPath = path,
                Phase = "Reading"
            });

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                list.Add(new AlbumArtBatchItemResult { FilePath = path ?? "", Ok = false, ErrorMessage = "Missing file." });
                continue;
            }

            Song? snap;
            try
            {
                snap = TrackMetadataLoader.LoadSong(path);
            }
            catch (Exception ex)
            {
                list.Add(new AlbumArtBatchItemResult
                    { FilePath = path, Ok = false, ErrorMessage = ex.Message });
                continue;
            }

            if (snap == null)
            {
                list.Add(new AlbumArtBatchItemResult
                    { FilePath = path, Ok = false, ErrorMessage = "Could not read metadata." });
                continue;
            }

            batchProgress?.Report(new AlbumArtBatchProgress
            {
                Done = i,
                Total = total,
                CurrentPath = path,
                Phase = "Fetching"
            });

            AlbumArtFetchBytesResult fetch;
            try
            {
                fetch = await FetchCoverBytesAsync(
                    path,
                    snap.Album,
                    snap.AlbumArtist,
                    snap.Artist,
                    snap.Year,
                    null,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                list.Add(new AlbumArtBatchItemResult
                    { FilePath = path, Ok = false, ErrorMessage = ex.Message });
                continue;
            }

            if (!fetch.Ok || fetch.ImageBytes is not { Length: > 0 } pic)
            {
                list.Add(new AlbumArtBatchItemResult
                    { FilePath = path, Ok = false, ErrorMessage = fetch.ErrorMessage ?? "Fetch failed." });
                continue;
            }

            batchProgress?.Report(new AlbumArtBatchProgress
            {
                Done = i,
                Total = total,
                CurrentPath = path,
                Phase = "Saving"
            });

            var playbackSnap = releasePlaybackForPath(path);
            try
            {
                if (!TrackMetadataSaver.TrySaveEmbeddedCoverOnly(path, pic, fetch.MusicBrainzReleaseId, out var err))
                {
                    list.Add(new AlbumArtBatchItemResult { FilePath = path, Ok = false, ErrorMessage = err ?? "Save failed." });
                }
                else
                {
                    list.Add(new AlbumArtBatchItemResult { FilePath = path, Ok = true });
                }
            }
            finally
            {
                restorePlayback(playbackSnap);
            }
        }

        batchProgress?.Report(new AlbumArtBatchProgress { Done = total, Total = total, Phase = "Done" });
        return list;
    }
}
