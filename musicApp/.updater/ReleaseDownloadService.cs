using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace musicApp.Updater;

internal static class ReleaseDownloadService
{
    internal const string UserAgent = "musicApp-updater (https://github.com/fosterbarnes/musicApp)";

    public static string FormatVersionForAsset(Version v) =>
        v.Revision >= 0 ? $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}" : $"{v.Major}.{v.Minor}.{v.Build}";

    public static string ReleaseTagSegment(string? releaseCodenameFromApi)
    {
        var s = releaseCodenameFromApi?.Trim();
        return string.IsNullOrEmpty(s) ? "untagged" : s;
    }

    public static string ExpectedAssetName(Version version, string? releaseCodenameSegment, VersionBuild kind)
    {
        var v = FormatVersionForAsset(version);
        var seg = ReleaseTagSegment(releaseCodenameSegment);
        return kind switch
        {
            VersionBuild.Portable => $"musicApp-v{v}-{seg}-portable.zip",
            VersionBuild.X64Installer => $"musicApp-v{v}-{seg}-x64-installer.exe",
            VersionBuild.X86Installer => $"musicApp-v{v}-{seg}-x86-installer.exe",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    public static string BuildReleaseAssetUrl(string tagName, string fileName)
    {
        var tag = tagName.Trim();
        if (!tag.StartsWith('v') && !tag.StartsWith('V'))
            tag = "v" + tag;
        var encTag = Uri.EscapeDataString(tag);
        var encFile = Uri.EscapeDataString(fileName);
        return $"https://github.com/fosterbarnes/musicApp/releases/download/{encTag}/{encFile}";
    }

    public static async Task<string> ResolveDownloadUrlAsync(
        HttpClient http,
        string tagName,
        string expectedFileName,
        Version version,
        VersionBuild kind,
        CancellationToken cancellationToken)
    {
        var direct = BuildReleaseAssetUrl(tagName, expectedFileName);
        try
        {
            using var head = new HttpRequestMessage(HttpMethod.Head, direct);
            using var resp = await http.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (resp.IsSuccessStatusCode)
                return resp.RequestMessage?.RequestUri?.ToString() ?? direct;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // fall through to API
        }

        var tag = tagName.Trim();
        if (!tag.StartsWith('v') && !tag.StartsWith('V'))
            tag = "v" + tag;
        var apiUrl = $"https://api.github.com/repos/fosterbarnes/musicApp/releases/tags/{Uri.EscapeDataString(tag)}";
        string json;
        try
        {
            json = await http.GetStringAsync(apiUrl, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return direct;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("assets", out var assets))
                return direct;

            string? matchUrl = null;
            foreach (var a in assets.EnumerateArray())
            {
                if (!a.TryGetProperty("name", out var nameEl))
                    continue;
                var name = nameEl.GetString();
                if (string.IsNullOrEmpty(name))
                    continue;
                if (!name.Equals(expectedFileName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (a.TryGetProperty("browser_download_url", out var urlEl)
                    && urlEl.GetString() is { } u)
                    matchUrl = u;
                break;
            }

            if (matchUrl != null)
                return matchUrl;

            var prefix = $"musicApp-v{FormatVersionForAsset(version)}-";
            var suffix = kind switch
            {
                VersionBuild.Portable => "-portable.zip",
                VersionBuild.X64Installer => "-x64-installer.exe",
                VersionBuild.X86Installer => "-x86-installer.exe",
                _ => ""
            };
            if (suffix.Length > 0)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    if (!a.TryGetProperty("name", out var nameEl))
                        continue;
                    var name = nameEl.GetString();
                    if (string.IsNullOrEmpty(name))
                        continue;
                    if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (a.TryGetProperty("browser_download_url", out var urlEl)
                        && urlEl.GetString() is { } u)
                        return u;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return direct;
        }

        return direct;
    }

    public static HttpClient CreateHttpClient()
    {
        var http = new HttpClient();
        http.Timeout = TimeSpan.FromMinutes(10);
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    public static async Task DownloadToFileAsync(
        HttpClient http,
        string url,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1L;
        await using var input = await resp.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[65536];
        long readTotal = 0;
        int n;
        while ((n = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, n), cancellationToken);
            readTotal += n;
            if (total > 0 && progress != null)
                progress.Report(readTotal / (double)total);
        }

        if (total > 0 && readTotal != total)
            throw new IOException($"Download incomplete ({readTotal} of {total} bytes).");

        progress?.Report(1);
    }
}
