using PlantMonitor.Backend.Repositories;
using Xunit;

namespace PlantMonitor.Backend.Tests;

[Collection(nameof(RepositoryCollection))]
public class ReadingRepositoryTests(StackFixture stack) : RepositoryTestBase(stack)
{
    private static ReadingRow Reading(string deviceId, int percent, DateTimeOffset receivedAt) =>
        new() { DeviceId = deviceId, Raw = percent * 50, Percent = percent, ReceivedAt = receivedAt };

    [Fact]
    public async Task Readings_latest_for_devices_returns_newest_per_device()
    {
        await using var ctx = await MigratedContextAsync();
        var repo = new ReadingRepository(ctx);
        var now = DateTimeOffset.UtcNow;
        await repo.AddAsync(Reading("rr-a", 20, now.AddHours(-2)), default);
        await repo.AddAsync(Reading("rr-a", 25, now.AddHours(-1)), default); // newer
        await repo.AddAsync(Reading("rr-b", 40, now.AddHours(-3)), default);

        var latest = await repo.GetLatestForDevicesAsync(["rr-a", "rr-b"], default);

        Assert.Equal(25, Assert.Single(latest, r => r.DeviceId == "rr-a").Percent);
        Assert.Equal(40, Assert.Single(latest, r => r.DeviceId == "rr-b").Percent);
    }

    [Fact]
    public async Task Readings_latest_returns_one_row_per_device_on_tied_timestamps()
    {
        await using var ctx = await MigratedContextAsync();
        var repo = new ReadingRepository(ctx);
        var tied = DateTimeOffset.UtcNow;
        await repo.AddAsync(Reading("tie", 20, tied), default);
        await repo.AddAsync(Reading("tie", 30, tied), default); // same instant

        var latest = await repo.GetLatestForDevicesAsync(["tie"], default);

        Assert.Single(latest); // exactly one survives the tie-break
    }

    [Fact]
    public async Task Delete_by_device_removes_all_its_readings_and_leaves_others()
    {
        await using var ctx = await MigratedContextAsync();
        var repo = new ReadingRepository(ctx);
        var now = DateTimeOffset.UtcNow;
        await repo.AddAsync(Reading("del-gone", 10, now.AddHours(-2)), default);
        await repo.AddAsync(Reading("del-gone", 20, now.AddHours(-1)), default);
        await repo.AddAsync(Reading("del-keep", 30, now), default);

        Assert.True(await repo.DeleteByDeviceAsync("del-gone", default));

        // Every reading for the device is gone, so it no longer appears as a sensor.
        Assert.Empty(await repo.GetReadingsAsync("del-gone", null, 50, default));
        Assert.DoesNotContain(await repo.GetAllLatestAsync(default), r => r.DeviceId == "del-gone");
        // The other device is untouched.
        Assert.Contains(await repo.GetAllLatestAsync(default), r => r.DeviceId == "del-keep");
        // Deleting again is a no-op (nothing left to remove).
        Assert.False(await repo.DeleteByDeviceAsync("del-gone", default));
    }

    [Fact]
    public async Task Reading_round_trips_the_firmware_version()
    {
        await using var ctx = await MigratedContextAsync();
        var repo = new ReadingRepository(ctx);
        await repo.AddAsync(
            new ReadingRow { DeviceId = "fw-dev", Raw = 1, Percent = 2, Fw = "firmware-v0.4.0" }, default);

        var latest = await repo.GetLatestForDeviceAsync("fw-dev", default);
        Assert.Equal("firmware-v0.4.0", latest!.Fw);
    }

    [Fact]
    public async Task Readings_filter_by_since_and_limit_newest_first()
    {
        await using var ctx = await MigratedContextAsync();
        var repo = new ReadingRepository(ctx);
        var now = DateTimeOffset.UtcNow;
        await repo.AddAsync(Reading("rf", 10, now.AddHours(-3)), default);
        await repo.AddAsync(Reading("rf", 20, now.AddHours(-2)), default);
        await repo.AddAsync(Reading("rf", 30, now.AddHours(-1)), default);

        var since = await repo.GetReadingsAsync("rf", now.AddHours(-2).AddMinutes(-1), 50, default);
        Assert.Equal([30, 20], since.Select(r => r.Percent));

        var limited = await repo.GetReadingsAsync("rf", null, 1, default);
        Assert.Equal(30, Assert.Single(limited).Percent);
    }
}
