using Microsoft.EntityFrameworkCore;
using PlantMonitor.Backend.Repositories;
using Xunit;

namespace PlantMonitor.Backend.Tests;

/// <summary>
/// Repository tests against a real postgres container — the LINQ they run
/// (the latest-per-device anti-join in particular) only proves out against
/// the actual provider.
/// </summary>
public class RepositoryTests(StackFixture stack) : IClassFixture<StackFixture>
{
    private AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(stack.Db.GetConnectionString()).Options;
        return new AppDbContext(options);
    }

    private async Task<AppDbContext> MigratedContextAsync()
    {
        var ctx = NewContext();
        await ctx.Database.MigrateAsync();
        return ctx;
    }

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
    public async Task Readings_unassigned_excludes_bound_devices()
    {
        await using var ctx = await MigratedContextAsync();
        var readings = new ReadingRepository(ctx);
        var plants = new PlantRepository(ctx);
        var now = DateTimeOffset.UtcNow;
        await readings.AddAsync(Reading("un-free", 30, now), default);
        await readings.AddAsync(Reading("un-bound", 50, now), default);
        await plants.AddAsync(new Plant { Name = "Bound", DeviceId = "un-bound" }, default);

        var unassigned = await readings.GetUnassignedLatestAsync(default);

        Assert.Contains(unassigned, r => r.DeviceId == "un-free");
        Assert.DoesNotContain(unassigned, r => r.DeviceId == "un-bound");
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

    [Fact]
    public async Task Plant_round_trips_with_species_and_device_uniqueness()
    {
        await using var ctx = await MigratedContextAsync();
        var plants = new PlantRepository(ctx);
        var species = new SpeciesRepository(ctx);

        var basil = await species.AddAsync("pr-basil", default);
        var plant = new Plant { Name = "Window basil", SpeciesId = basil.Id, DeviceId = "pr-dev" };
        await plants.AddAsync(plant, default);

        var loaded = await plants.GetByIdAsync(plant.Id, default);
        Assert.Equal("pr-basil", loaded!.Species!.Name);

        Assert.True(await plants.DeviceTakenAsync("pr-dev", null, default));
        Assert.False(await plants.DeviceTakenAsync("pr-dev", plant.Id, default)); // excludes self

        loaded.Name = "Shelf basil";
        await plants.UpdateAsync(loaded, default);
        Assert.Equal("Shelf basil", (await plants.GetByIdAsync(plant.Id, default))!.Name);

        Assert.True(await plants.DeleteAsync(plant.Id, default));
        Assert.Null(await plants.GetByIdAsync(plant.Id, default));
        Assert.False(await plants.DeleteAsync(plant.Id, default)); // already gone
    }

    [Fact]
    public async Task Species_add_then_find_by_name()
    {
        await using var ctx = await MigratedContextAsync();
        var repo = new SpeciesRepository(ctx);

        var added = await repo.AddAsync("sr-fern", default);
        Assert.NotEqual(Guid.Empty, added.Id);

        var found = await repo.FindByNameAsync("sr-fern", default);
        Assert.Equal(added.Id, found!.Id);
        Assert.Null(await repo.FindByNameAsync("sr-missing", default));
    }
}
