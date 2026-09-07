using Microsoft.EntityFrameworkCore;
using PlantMonitor.Backend.Repositories;
using Xunit;

namespace PlantMonitor.Backend.Tests;

[Collection(nameof(RepositoryCollection))]
public class PlantRepositoryTests(StackFixture stack) : RepositoryTestBase(stack)
{
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
    public async Task Plant_notified_status_round_trips_as_text()
    {
        await using var ctx = await MigratedContextAsync();
        var repo = new PlantRepository(ctx);
        var plant = new Plant { Name = $"ns-{Guid.NewGuid()}", NotifiedStatus = WaterStatus.Must };
        await repo.AddAsync(plant, default);

        await using var fresh = NewContext();
        var stored = await fresh.Plants.FirstAsync(p => p.Id == plant.Id);

        Assert.Equal(WaterStatus.Must, stored.NotifiedStatus);
        var raw = await fresh.Database
            .SqlQuery<string?>($"SELECT notified_status AS \"Value\" FROM plants WHERE id = {plant.Id}")
            .SingleAsync();
        Assert.Equal("Must", raw);
    }
}
