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
}
