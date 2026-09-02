using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using PlantMonitor.Backend.Repositories;

namespace PlantMonitor.Backend;

/// <summary>
/// Polls GitHub Releases for firmware images and caches them in Postgres, so
/// devices can fetch an update from the local network — they have no TLS and
/// cannot reach GitHub themselves. Only ever inserts: an image already cached
/// is never re-downloaded.
/// </summary>
public sealed class FirmwareFetchWorker(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ILogger<FirmwareFetchWorker> log) : BackgroundService
{
    /// <summary>Release tags for the firmware component; the app releases share the repo.</summary>
    private const string TagPrefix = "firmware-v";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var repo = config["Firmware:GithubRepo"];
        if (string.IsNullOrWhiteSpace(repo))
        {
            log.LogInformation("Firmware:GithubRepo is not configured; firmware caching is off");
            return;
        }

        var interval = TimeSpan.FromMinutes(config.GetValue("Firmware:PollMinutes", 30));

        while (!ct.IsCancellationRequested)
        {
            // Every failure mode lands here: GitHub unreachable, a malformed
            // release, or — right after a fresh deploy — the table not yet
            // created by IngestWorker's migration. All are retried on the next
            // tick; devices only ask hourly, so nothing is lost by waiting.
            try
            {
                await PollAsync(repo, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning("Firmware poll of {Repo} failed: {Message}", repo, ex.Message);
            }

            await Task.Delay(interval, ct);
        }
    }

    private async Task PollAsync(string repo, CancellationToken ct)
    {
        using var http = CreateClient();

        var releases = await http.GetStringAsync(
            $"https://api.github.com/repos/{repo}/releases?per_page=30", ct);

        if (FindLatestImage(releases) is not { } latest)
            return;
        var (tag, url) = latest;

        await using var scope = scopeFactory.CreateAsyncScope();
        var firmware = scope.ServiceProvider.GetRequiredService<IFirmwareRepository>();
        if (await firmware.ExistsAsync(tag, ct))
            return;

        var data = await http.GetByteArrayAsync(url, ct);
        var sha = Convert.ToHexStringLower(SHA256.HashData(data));

        await firmware.AddAsync(new FirmwareImage
        {
            Version = tag,
            Sha256 = sha,
            Size = data.Length,
            Data = data,
            FetchedAt = DateTimeOffset.UtcNow,
        }, ct);

        log.LogInformation("Cached firmware {Version} ({Size} bytes, sha256 {Sha})", tag, data.Length, sha);
    }

    private HttpClient CreateClient()
    {
        var http = httpFactory.CreateClient();
        // GitHub rejects requests without a User-Agent.
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("plant-monitor", AppVersion.Resolve(config)));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (config["Firmware:GithubToken"] is { Length: > 0 } token)
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    /// <summary>
    /// The newest published firmware release carrying a .bin asset, or null.
    /// GitHub returns releases newest first. Drafts and prereleases are skipped:
    /// a device must never install something not meant to ship.
    /// </summary>
    private static (string Tag, string Url)? FindLatestImage(string releasesJson)
    {
        using var doc = JsonDocument.Parse(releasesJson);
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
                continue;
            if (release.GetProperty("tag_name").GetString() is not { } tag || !tag.StartsWith(TagPrefix))
                continue;

            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString()?.EndsWith(".bin") == true
                    && asset.GetProperty("browser_download_url").GetString() is { } url)
                    return (tag, url);
            }
        }

        return null;
    }
}
