using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace musicApp.Helpers;

internal static class StorefrontAlbumArtFallback
{
    /// <summary>Apple Search API; host must stay <c>itunes.apple.com</c>.</summary>
    private const string FruitAppStorefrontSearchUrlFormat =
        "https://itunes.apple.com/search?term={0}&entity=album&limit=8&country=us";

    internal static async Task<byte[]?> TryDownloadFromFruitAppStorefrontAsync(
        HttpClient http,
        string artist,
        string album,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(album))
            return null;

        var artistT = (artist ?? "").Trim();
        var albumT = album.Trim();

        var first = await TryDownloadFromFruitAppSearchTermAsync(http, artistT, albumT, cancellationToken)
            .ConfigureAwait(false);
        if (first is { Length: > 0 })
            return first;

        if (!string.IsNullOrEmpty(artistT))
        {
            return await TryDownloadFromFruitAppSearchTermAsync(http, "", albumT, cancellationToken)
                .ConfigureAwait(false);
        }

        return null;
    }

    private static async Task<byte[]?> TryDownloadFromFruitAppSearchTermAsync(
        HttpClient http,
        string artistT,
        string albumT,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(albumT))
            return null;

        var term = string.IsNullOrEmpty(artistT) ? albumT : $"{artistT} {albumT}";
        var url = string.Format(FruitAppStorefrontSearchUrlFormat, Uri.EscapeDataString(term));

        string json;
        try
        {
            json = await http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                return null;

            foreach (var el in results.EnumerateArray())
            {
                if (!el.TryGetProperty("artworkUrl100", out var artEl))
                    continue;
                var artUrl = artEl.GetString();
                if (string.IsNullOrEmpty(artUrl))
                    continue;
                var hi = artUrl.Replace("100x100", "600x600", StringComparison.OrdinalIgnoreCase);
                try
                {
                    return await http.GetByteArrayAsync(hi, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    try
                    {
                        return await http.GetByteArrayAsync(artUrl, cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        continue;
                    }
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    internal static async Task<byte[]?> TryDownloadFromDeezerAsync(
        HttpClient http,
        string artist,
        string album,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(album))
            return null;

        var artistT = (artist ?? "").Trim();
        var albumT = album.Trim();
        var q = string.IsNullOrEmpty(artistT) ? albumT : $"{artistT} {albumT}";
        var url = "https://api.deezer.com/search/album?q=" + Uri.EscapeDataString(q);

        string json;
        try
        {
            json = await http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
                return null;

            foreach (var el in data.EnumerateArray())
            {
                string? coverUrl = null;
                foreach (var prop in new[] { "cover_xl", "cover_big", "cover_medium" })
                {
                    if (el.TryGetProperty(prop, out var u) && u.ValueKind == JsonValueKind.String)
                    {
                        coverUrl = u.GetString();
                        break;
                    }
                }

                if (string.IsNullOrEmpty(coverUrl))
                    continue;

                try
                {
                    return await http.GetByteArrayAsync(coverUrl, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }
}
