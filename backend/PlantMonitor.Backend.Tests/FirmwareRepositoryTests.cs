using PlantMonitor.Backend.Repositories;
using Xunit;

namespace PlantMonitor.Backend.Tests;

[Collection(nameof(RepositoryCollection))]
public class FirmwareRepositoryTests(StackFixture stack) : RepositoryTestBase(stack)
{
    private static FirmwareImage Image(string version, byte[] data, DateTimeOffset fetchedAt) =>
        new() { Version = version, Sha256 = "sha-" + version, Size = data.Length, Data = data, FetchedAt = fetchedAt };

    [Fact]
    public async Task Firmware_latest_is_the_most_recently_fetched_image()
    {
        await using var ctx = await MigratedContextAsync();
        var repo = new FirmwareRepository(ctx);
        var now = DateTimeOffset.UtcNow;
        var older = $"fr-v1-{Guid.NewGuid()}";
        var newer = $"fr-v2-{Guid.NewGuid()}";
        await repo.AddAsync(Image(older, [1, 2], now.AddDays(-1)), default);
        await repo.AddAsync(Image(newer, [1, 2, 3], now), default);

        var latest = await repo.GetLatestAsync(default);

        Assert.Equal(newer, latest!.Version);
        Assert.Equal(3, latest.Size);
    }

    [Fact]
    public async Task Firmware_data_round_trips_and_is_found_by_version()
    {
        await using var ctx = await MigratedContextAsync();
        var repo = new FirmwareRepository(ctx);
        var version = $"fr-data-{Guid.NewGuid()}";
        byte[] data = [0xE9, 0x00, 0xFF, 0x42];
        await repo.AddAsync(Image(version, data, DateTimeOffset.UtcNow), default);

        Assert.Equal(data, await repo.GetDataAsync(version, default));
        Assert.True(await repo.ExistsAsync(version, default));
        Assert.Null(await repo.GetDataAsync("fr-missing", default));
        Assert.False(await repo.ExistsAsync("fr-missing", default));
    }
}
