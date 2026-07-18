using NSubstitute;
using PlantMonitor.Backend.Dtos;
using PlantMonitor.Backend.Repositories;
using PlantMonitor.Backend.Services;
using Xunit;

namespace PlantMonitor.Backend.Tests;

public class PlantServiceTests
{
    private readonly IPlantRepository plants = Substitute.For<IPlantRepository>();
    private readonly ISpeciesRepository species = Substitute.For<ISpeciesRepository>();
    private readonly IReadingRepository readings = Substitute.For<IReadingRepository>();
    private PlantService Service => new(plants, species, readings);

    private static PlantInput Input(string name = "Basil", string? speciesName = null,
        string? location = null, string? sun = null, string? deviceId = null,
        int? mustWater = null, int? canWater = null) =>
        new(name, speciesName, location, sun, deviceId, mustWater, canWater);

    [Fact]
    public async Task Create_reuses_an_existing_species()
    {
        var existing = new Species { Id = Guid.NewGuid(), Name = "Basil" };
        species.FindByNameAsync("Basil", Arg.Any<CancellationToken>()).Returns(existing);
        plants.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new Plant { Name = "Pot", Species = existing });

        var result = await Service.CreateAsync(Input(speciesName: "Basil"), default);

        Assert.Equal(PlantWriteStatus.Ok, result.Status);
        await species.DidNotReceive().AddAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_adds_a_new_species_and_links_it()
    {
        var created = new Species { Id = Guid.NewGuid(), Name = "Genovese" };
        species.FindByNameAsync("Genovese", Arg.Any<CancellationToken>()).Returns((Species?)null);
        species.AddAsync("Genovese", Arg.Any<CancellationToken>()).Returns(created);
        Plant? saved = null;
        plants.When(p => p.AddAsync(Arg.Any<Plant>(), Arg.Any<CancellationToken>()))
            .Do(ci => saved = ci.Arg<Plant>());
        plants.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new Plant { Name = "Pot" });

        var result = await Service.CreateAsync(Input(speciesName: "  Genovese  "), default);

        Assert.Equal(PlantWriteStatus.Ok, result.Status);
        await species.Received().AddAsync("Genovese", Arg.Any<CancellationToken>());
        Assert.Equal(created.Id, saved!.SpeciesId);
    }

    [Fact]
    public async Task Create_rejects_a_taken_sensor()
    {
        plants.DeviceTakenAsync("dev", null, Arg.Any<CancellationToken>()).Returns(true);

        var result = await Service.CreateAsync(Input(deviceId: "dev"), default);

        Assert.Equal(PlantWriteStatus.DeviceConflict, result.Status);
        await plants.DidNotReceive().AddAsync(Arg.Any<Plant>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_blanks_become_null()
    {
        Plant? saved = null;
        plants.When(p => p.AddAsync(Arg.Any<Plant>(), Arg.Any<CancellationToken>()))
            .Do(ci => saved = ci.Arg<Plant>());
        plants.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new Plant { Name = "Pot" });

        await Service.CreateAsync(Input(location: "  ", sun: "", deviceId: "  "), default);

        Assert.Null(saved!.Location);
        Assert.Null(saved.SunExposure);
        Assert.Null(saved.DeviceId);
    }

    [Fact]
    public async Task Create_saves_watering_limits()
    {
        Plant? saved = null;
        plants.When(p => p.AddAsync(Arg.Any<Plant>(), Arg.Any<CancellationToken>()))
            .Do(ci => saved = ci.Arg<Plant>());
        plants.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new Plant { Name = "Pot" });

        await Service.CreateAsync(Input(mustWater: 20, canWater: 40), default);

        Assert.Equal(20, saved!.MustWaterPercent);
        Assert.Equal(40, saved.CanWaterPercent);
    }

    [Fact]
    public async Task Create_rejects_must_above_can()
    {
        var result = await Service.CreateAsync(Input(mustWater: 50, canWater: 40), default);

        Assert.Equal(PlantWriteStatus.InvalidLimits, result.Status);
        await plants.DidNotReceive().AddAsync(Arg.Any<Plant>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_returns_not_found_when_missing()
    {
        plants.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Plant?)null);

        var result = await Service.UpdateAsync(Guid.NewGuid(), Input(), default);

        Assert.Equal(PlantWriteStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Update_applies_fields()
    {
        var id = Guid.NewGuid();
        var plant = new Plant { Id = id, Name = "Old" };
        plants.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(plant);

        var result = await Service.UpdateAsync(id, Input(name: "New", location: "Shelf"), default);

        Assert.Equal(PlantWriteStatus.Ok, result.Status);
        Assert.Equal("New", plant.Name);
        Assert.Equal("Shelf", plant.Location);
        await plants.Received().UpdateAsync(plant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_plants_attaches_the_latest_reading_per_device()
    {
        var withSensor = new Plant { Id = Guid.NewGuid(), Name = "A", DeviceId = "d1" };
        var noSensor = new Plant { Id = Guid.NewGuid(), Name = "B", DeviceId = null };
        plants.GetAllAsync(Arg.Any<CancellationToken>()).Returns([withSensor, noSensor]);
        var reading = new ReadingRow { DeviceId = "d1", Percent = 55, ReceivedAt = DateTimeOffset.UtcNow };
        readings.GetLatestForDevicesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns([reading]);

        var result = await Service.GetPlantsAsync(default);

        Assert.Equal(55, result.Single(p => p.Plant.DeviceId == "d1").Latest!.Percent);
        Assert.Null(result.Single(p => p.Plant.DeviceId == null).Latest);
    }

    [Fact]
    public async Task Delete_delegates_to_the_repository()
    {
        var id = Guid.NewGuid();
        plants.DeleteAsync(id, Arg.Any<CancellationToken>()).Returns(true);

        Assert.True(await Service.DeleteAsync(id, default));
    }
}

public class ThinServiceTests
{
    [Fact]
    public async Task Sensor_service_returns_unassigned_from_the_repository()
    {
        var readings = Substitute.For<IReadingRepository>();
        var rows = new List<ReadingRow> { new() { DeviceId = "x" } };
        readings.GetUnassignedLatestAsync(Arg.Any<CancellationToken>()).Returns(rows);

        Assert.Same(rows, await new SensorService(readings).GetUnassignedAsync(default));
    }

    [Fact]
    public async Task Reading_service_records_a_reading_via_the_repository()
    {
        var readings = Substitute.For<IReadingRepository>();
        var stored = await new ReadingService(readings).RecordAsync(new Reading("plant-1", 3000, 60), default);

        Assert.True(stored);
        await readings.Received().AddAsync(
            Arg.Is<ReadingRow>(r => r != null && r.DeviceId == "plant-1" && r.Raw == 3000 && r.Percent == 60),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reading_service_drops_a_duplicate_within_the_dedup_window()
    {
        var readings = Substitute.For<IReadingRepository>();
        readings.GetLatestForDeviceAsync("plant-1", Arg.Any<CancellationToken>())
            .Returns(new ReadingRow { DeviceId = "plant-1", ReceivedAt = DateTimeOffset.UtcNow.AddMinutes(-1) });

        var stored = await new ReadingService(readings).RecordAsync(new Reading("plant-1", 3000, 60), default);

        Assert.False(stored);
        await readings.DidNotReceive().AddAsync(Arg.Any<ReadingRow>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reading_service_stores_when_the_last_reading_is_old()
    {
        var readings = Substitute.For<IReadingRepository>();
        readings.GetLatestForDeviceAsync("plant-1", Arg.Any<CancellationToken>())
            .Returns(new ReadingRow { DeviceId = "plant-1", ReceivedAt = DateTimeOffset.UtcNow.AddHours(-1) });

        var stored = await new ReadingService(readings).RecordAsync(new Reading("plant-1", 3000, 60), default);

        Assert.True(stored);
        await readings.Received().AddAsync(Arg.Any<ReadingRow>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Species_service_lists_from_the_repository()
    {
        var species = Substitute.For<ISpeciesRepository>();
        var rows = new List<Species> { new() { Name = "Fern" } };
        species.GetAllAsync(Arg.Any<CancellationToken>()).Returns(rows);

        Assert.Same(rows, await new SpeciesService(species).GetAllAsync(default));
    }
}
