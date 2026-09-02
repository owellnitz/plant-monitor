using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PlantMonitor.Backend.Repositories;
using Xunit;

namespace PlantMonitor.Backend.Tests;

/// <summary>
/// Runs the real worker against a stubbed GitHub. Everything the device later
/// trusts — the version it is offered, the SHA-256 it verifies against — is
/// produced here, so these cover the mapping end to end.
/// </summary>
public class FirmwareFetchWorkerTests
{
    private const string AssetUrl = "https://github.test/download/firmware-v0.4.0.bin";
    private static readonly byte[] ImageBytes = Encoding.UTF8.GetBytes("ESP32 image bytes");

    private readonly IFirmwareRepository firmware = Substitute.For<IFirmwareRepository>();

    /// <summary>Serves canned responses per URL and records what was requested.</summary>
    private sealed class StubGitHub(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Requested.Add(url);
            return Task.FromResult(respond(url));
        }
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static string Release(string tag, string assetName = "firmware-v0.4.0.bin",
        bool draft = false, bool prerelease = false) =>
        $$"""
        {"tag_name":"{{tag}}","draft":{{(draft ? "true" : "false")}},
         "prerelease":{{(prerelease ? "true" : "false")}},
         "assets":[{"name":"{{assetName}}","browser_download_url":"{{AssetUrl}}"}]}
        """;

    private async Task<StubGitHub> RunAsync(string releasesJson, string? repo = "owner/repo",
        HttpStatusCode releasesStatus = HttpStatusCode.OK)
    {
        var stub = new StubGitHub(url => url.Contains("api.github.com")
            ? (releasesStatus == HttpStatusCode.OK
                ? Json(releasesJson)
                : new HttpResponseMessage(releasesStatus))
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ImageBytes) });

        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(stub, disposeHandler: false));

        var services = new ServiceCollection();
        services.AddScoped(_ => firmware);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var settings = new Dictionary<string, string?> { ["Firmware:PollMinutes"] = "60" };
        if (repo is not null)
            settings["Firmware:GithubRepo"] = repo;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var worker = new FirmwareFetchWorker(scopeFactory, httpFactory, config,
            NullLogger<FirmwareFetchWorker>.Instance);

        // One poll runs immediately; the next is an hour out, so stopping here
        // never cuts a second poll short.
        await worker.StartAsync(CancellationToken.None);
        if (repo is not null)
            await WaitForPollAsync(stub);
        await worker.StopAsync(CancellationToken.None);
        return stub;
    }

    /// <summary>
    /// Waits for the poll to run to completion, so the test never stops the
    /// worker mid-download. The releases request marks the poll's start; after
    /// that it is followed to quiescence — one sample with no new request and
    /// no new repository call, everything in between being in-memory.
    /// </summary>
    private async Task WaitForPollAsync(StubGitHub stub)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && !stub.Requested.Exists(u => u.Contains("api.github.com")))
            await Task.Delay(20);

        Assert.True(stub.Requested.Exists(u => u.Contains("api.github.com")),
            "Worker never queried GitHub within 10s");

        var previous = -1;
        while (DateTime.UtcNow < deadline)
        {
            var activity = stub.Requested.Count + firmware.ReceivedCalls().Count();
            if (activity == previous)
                return;
            previous = activity;
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task Caches_a_new_release_with_its_sha256_and_size()
    {
        firmware.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        FirmwareImage? saved = null;
        firmware.When(f => f.AddAsync(Arg.Any<FirmwareImage>(), Arg.Any<CancellationToken>()))
            .Do(ci => saved = ci.Arg<FirmwareImage>());

        await RunAsync($"[{Release("firmware-v0.4.0")}]");

        Assert.NotNull(saved);
        Assert.Equal("firmware-v0.4.0", saved.Version);
        Assert.Equal(ImageBytes, saved.Data);
        Assert.Equal(ImageBytes.Length, saved.Size);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(ImageBytes)), saved.Sha256);
    }

    [Fact]
    public async Task Already_cached_release_is_not_downloaded_again()
    {
        firmware.ExistsAsync("firmware-v0.4.0", Arg.Any<CancellationToken>()).Returns(true);

        var stub = await RunAsync($"[{Release("firmware-v0.4.0")}]");

        await firmware.DidNotReceive().AddAsync(Arg.Any<FirmwareImage>(), Arg.Any<CancellationToken>());
        Assert.DoesNotContain(AssetUrl, stub.Requested);
    }

    [Fact]
    public async Task Drafts_and_prereleases_are_skipped_for_the_published_release()
    {
        firmware.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        FirmwareImage? saved = null;
        firmware.When(f => f.AddAsync(Arg.Any<FirmwareImage>(), Arg.Any<CancellationToken>()))
            .Do(ci => saved = ci.Arg<FirmwareImage>());

        await RunAsync($"""
            [{Release("firmware-v0.6.0", draft: true)},
             {Release("firmware-v0.5.0", prerelease: true)},
             {Release("firmware-v0.4.0")}]
            """);

        Assert.Equal("firmware-v0.4.0", saved!.Version);
    }

    // The app and the firmware share the repo and its release feed.
    [Fact]
    public async Task App_releases_are_ignored()
    {
        await RunAsync($"[{Release("app-v1.3.0")}, {Release("firmware-v0.4.0")}]");

        await firmware.Received().ExistsAsync("firmware-v0.4.0", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_release_without_a_bin_asset_is_ignored()
    {
        await RunAsync($"[{Release("firmware-v0.4.0", assetName: "notes.txt")}]");

        await firmware.DidNotReceive().AddAsync(Arg.Any<FirmwareImage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_github_error_leaves_the_worker_running()
    {
        var stub = await RunAsync("[]", releasesStatus: HttpStatusCode.InternalServerError);

        await firmware.DidNotReceive().AddAsync(Arg.Any<FirmwareImage>(), Arg.Any<CancellationToken>());
        Assert.NotEmpty(stub.Requested);
    }

    [Fact]
    public async Task Nothing_is_polled_when_no_repo_is_configured()
    {
        var stub = await RunAsync($"[{Release("firmware-v0.4.0")}]", repo: null);

        Assert.Empty(stub.Requested);
    }
}
