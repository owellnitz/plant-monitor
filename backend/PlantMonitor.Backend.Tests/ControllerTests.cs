using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using PlantMonitor.Backend.Controllers;
using PlantMonitor.Backend.Dtos;
using PlantMonitor.Backend.Services;
using Xunit;

namespace PlantMonitor.Backend.Tests;

public class PlantsControllerTests
{
    private readonly IPlantService service = Substitute.For<IPlantService>();
    private PlantsController Controller => new(service);

    private static PlantWithReading View(string name = "Basil", string? species = "Genovese",
        int? percent = 55, string? deviceId = "d1") =>
        new(
            new Plant
            {
                Id = Guid.NewGuid(),
                Name = name,
                Species = species is null ? null : new Species { Name = species },
                DeviceId = deviceId,
            },
            percent is null ? null : new ReadingRow { DeviceId = deviceId!, Percent = percent.Value });

    private static PlantInput Input() => new("Basil", "Genovese", "Kitchen", "Full sun", "d1");

    [Fact]
    public async Task Get_by_id_maps_species_and_latest_reading()
    {
        service.GetPlantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(View());

        var result = await Controller.GetById(Guid.NewGuid(), default);

        var dto = Assert.IsType<PlantDto>(result.Value);
        Assert.Equal("Genovese", dto.Species);
        Assert.Equal(55, dto.Percent);
    }

    [Fact]
    public async Task Get_by_id_returns_404_when_missing()
    {
        service.GetPlantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PlantWithReading?)null);

        var result = await Controller.GetById(Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Create_returns_201()
    {
        service.CreateAsync(Arg.Any<PlantInput>(), Arg.Any<CancellationToken>())
            .Returns(new PlantWriteResult(PlantWriteStatus.Ok, View()));

        var result = await Controller.Create(Input(), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task Create_returns_409_on_conflict()
    {
        service.CreateAsync(Arg.Any<PlantInput>(), Arg.Any<CancellationToken>())
            .Returns(new PlantWriteResult(PlantWriteStatus.DeviceConflict, null));

        var result = await Controller.Create(Input(), default);

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_returns_400_on_invalid_limits()
    {
        service.CreateAsync(Arg.Any<PlantInput>(), Arg.Any<CancellationToken>())
            .Returns(new PlantWriteResult(PlantWriteStatus.InvalidLimits, null));

        var result = await Controller.Create(Input(), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Update_maps_statuses()
    {
        service.UpdateAsync(Arg.Any<Guid>(), Arg.Any<PlantInput>(), Arg.Any<CancellationToken>())
            .Returns(new PlantWriteResult(PlantWriteStatus.NotFound, null));
        Assert.IsType<NotFoundResult>((await Controller.Update(Guid.NewGuid(), Input(), default)).Result);

        service.UpdateAsync(Arg.Any<Guid>(), Arg.Any<PlantInput>(), Arg.Any<CancellationToken>())
            .Returns(new PlantWriteResult(PlantWriteStatus.DeviceConflict, null));
        Assert.IsType<ConflictObjectResult>((await Controller.Update(Guid.NewGuid(), Input(), default)).Result);

        service.UpdateAsync(Arg.Any<Guid>(), Arg.Any<PlantInput>(), Arg.Any<CancellationToken>())
            .Returns(new PlantWriteResult(PlantWriteStatus.Ok, View(name: "Renamed")));
        var ok = await Controller.Update(Guid.NewGuid(), Input(), default);
        Assert.Equal("Renamed", Assert.IsType<PlantDto>(ok.Value).Name);
    }

    [Fact]
    public async Task Delete_maps_204_and_404()
    {
        service.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        Assert.IsType<NoContentResult>(await Controller.Delete(Guid.NewGuid(), default));

        service.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        Assert.IsType<NotFoundResult>(await Controller.Delete(Guid.NewGuid(), default));
    }
}

public class ReadEndpointControllerTests
{
    [Fact]
    public async Task Sensors_controller_maps_unassigned_rows()
    {
        var service = Substitute.For<ISensorService>();
        service.GetUnassignedAsync(Arg.Any<CancellationToken>())
            .Returns([new ReadingRow { DeviceId = "x", Raw = 100, Percent = 20, ReceivedAt = DateTimeOffset.UtcNow }]);

        var sensors = await new SensorsController(service).GetUnassigned(default);

        var sensor = Assert.Single(sensors);
        Assert.Equal("x", sensor.DeviceId);
        Assert.Equal(20, sensor.Percent);
    }

    [Fact]
    public async Task Readings_controller_maps_rows()
    {
        var service = Substitute.For<IReadingService>();
        var id = Guid.NewGuid();
        service.GetReadingsAsync("x", null, 50, Arg.Any<CancellationToken>())
            .Returns([new ReadingRow { Id = id, DeviceId = "x", Raw = 100, Percent = 20 }]);

        var readings = await new ReadingsController(service).Get("x", null, 50, default);

        Assert.Equal(id, Assert.Single(readings).Id);
    }

    [Fact]
    public async Task Species_controller_maps_rows()
    {
        var service = Substitute.For<ISpeciesService>();
        var id = Guid.NewGuid();
        service.GetAllAsync(Arg.Any<CancellationToken>()).Returns([new Species { Id = id, Name = "Fern" }]);

        var species = await new SpeciesController(service).Get(default);

        var dto = Assert.Single(species);
        Assert.Equal(id, dto.Id);
        Assert.Equal("Fern", dto.Name);
    }
}

public class VersionControllerTests
{
    [Fact]
    public void Returns_the_baked_in_version()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["APP_VERSION"] = "1.2.3" })
            .Build();

        Assert.Equal("1.2.3", new VersionController(config).Get().Version);
    }

    [Fact]
    public void Falls_back_to_dev_version_when_unset()
    {
        Assert.Equal("0.0.0-dev", new VersionController(new ConfigurationBuilder().Build()).Get().Version);
    }
}
