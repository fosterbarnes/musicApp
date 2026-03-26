using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;

namespace musicApp.Updater;

internal static class GitHubTagsVersionService
{
    private const string ReleasesUrl = "https://api.github.com/repos/fosterbarnes/musicApp/releases";
    private const string UserAgent = "musicApp-updater (https://github.com/fosterbarnes/musicApp)";

    /// <summary>
    /// Uses GitHub <b>releases</b> (not raw git tags). Drafts are skipped; prereleases are included.
    /// The latest release is the one with the greatest <c>published_at</c> (then semver on the tag name if tied).
    /// </summary>
    public static async Task<RemoteVersionResult> FetchLatestVersionTagAsync(CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromSeconds(60);
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        Version? bestVersion = null;
        DateTimeOffset? bestPublished = null;
        string? bestTagName = null;
        string? bestReleaseName = null;

        for (var page = 1; page <= 10; page++)
        {
            var url = $"{ReleasesUrl}?per_page=100&page={page}";
            string json;
            try
            {
                json = await http.GetStringAsync(url, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new RemoteVersionResult { Error = $"Could not reach GitHub: {ex.Message}" };
            }

            List<GitHubReleaseJson>? releases;
            try
            {
                releases = JsonSerializer.Deserialize<List<GitHubReleaseJson>>(json, jsonOptions);
            }
            catch (Exception ex)
            {
                return new RemoteVersionResult { Error = $"Unexpected response from GitHub: {ex.Message}" };
            }

            if (releases == null || releases.Count == 0)
                break;

            foreach (var r in releases)
            {
                if (r.Draft)
                    continue;
                if (string.IsNullOrWhiteSpace(r.TagName) || string.IsNullOrWhiteSpace(r.PublishedAt))
                    continue;
                if (!VersionComparer.TryParse(r.TagName, out var v))
                    continue;
                if (!DateTimeOffset.TryParse(r.PublishedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var published))
                    continue;

                if (bestPublished is null || bestVersion is null)
                {
                    bestPublished = published;
                    bestVersion = v;
                    bestTagName = r.TagName.Trim();
                    bestReleaseName = r.Name?.Trim();
                    continue;
                }

                if (published > bestPublished.Value)
                {
                    bestPublished = published;
                    bestVersion = v;
                    bestTagName = r.TagName.Trim();
                    bestReleaseName = r.Name?.Trim();
                    continue;
                }

                if (published == bestPublished.Value && v.CompareTo(bestVersion) > 0)
                {
                    bestVersion = v;
                    bestPublished = published;
                    bestTagName = r.TagName.Trim();
                    bestReleaseName = r.Name?.Trim();
                }
            }

            if (releases.Count < 100)
                break;
        }

        if (bestVersion is null || bestPublished is null)
            return new RemoteVersionResult { Error = "No published releases on GitHub (drafts are ignored)." };

        return new RemoteVersionResult
        {
            LatestVersion = bestVersion,
            LatestTagName = bestTagName,
            LatestReleaseVersionTag = TryParseVersionTagFromReleaseName(bestReleaseName, bestVersion),
            PublishedAt = bestPublished
        };
    }

    /// <summary>Git release tag for API paths, e.g. <c>v1.2.3</c>.</summary>
    internal static string TagNameFromVersion(Version v)
    {
        var core = v.Revision >= 0
            ? $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}"
            : $"{v.Major}.{v.Minor}.{v.Build}";
        return "v" + core;
    }

    /// <summary><c>GET /releases/tags/{tag}</c>; draft releases return 404.</summary>
    internal static async Task<DateTimeOffset?> TryFetchReleasePublishedAtForTagAsync(string tagName, CancellationToken cancellationToken = default)
    {
        var tag = tagName.Trim();
        if (!tag.StartsWith('v') && !tag.StartsWith('V'))
            tag = "v" + tag;
        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromSeconds(60);
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        var url = $"https://api.github.com/repos/fosterbarnes/musicApp/releases/tags/{Uri.EscapeDataString(tag)}";
        try
        {
            var json = await http.GetStringAsync(url, cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("published_at", out var pEl))
                return null;
            var s = pEl.GetString();
            if (string.IsNullOrEmpty(s))
                return null;
            if (!DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
                return null;
            return dto;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryParseVersionTagFromReleaseName(string? releaseName, Version version)
    {
        if (string.IsNullOrWhiteSpace(releaseName))
            return null;

        var parts = releaseName.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
            return null;
        if (!parts[0].Equals("musicApp", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!VersionComparer.TryParse(parts[1], out var nameVer) || nameVer != version)
            return null;

        return string.Join(" ", parts.Skip(2));
    }
}
