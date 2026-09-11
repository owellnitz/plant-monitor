using Microsoft.AspNetCore.Http;
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
    [Theory]
    [InlineData(SensorDeleteResult.Deleted, typeof(NoContentResult))]
    [InlineData(SensorDeleteResult.Assigned, typeof(ConflictObjectResult))]
    [InlineData(SensorDeleteResult.NotFound, typeof(NotFoundResult))]
    public async Task Sensors_controller_maps_delete_result(SensorDeleteResult result, Type expected)
    {
        var service = Substitute.For<ISensorService>();
        service.DeleteAsync("x", Arg.Any<CancellationToken>()).Returns(result);

        var response = await new SensorsController(service).Delete("x", default);

        Assert.IsType(expected, response);
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

public class PushControllerTests
{
    private readonly IPushService service = Substitute.For<IPushService>();
    private PushController Controller => new(service);

    [Fact]
    public void Serves_the_configured_vapid_public_key()
    {
        service.PublicKey.Returns("BN4-public-key");

        Assert.Equal("BN4-public-key", Controller.GetVapidKey().Value?.PublicKey);
    }

    [Fact]
    public void Reports_unavailable_when_push_is_not_configured()
    {
        service.PublicKey.Returns((string?)null);

        var result = Assert.IsType<ObjectResult>(Controller.GetVapidKey().Result);

        // The frontend hides the notification toggle on exactly this status.
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
    }

    [Fact]
    public async Task Stores_a_subscription()
    {
        var input = new PushSubscriptionInput("https://push.example/x", "key", "auth");

        Assert.IsType<NoContentResult>(await Controller.Subscribe(input, default));
        await service.Received().SubscribeAsync(input, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true, typeof(NoContentResult))]
    [InlineData(false, typeof(NotFoundResult))]
    public async Task Unsubscribe_reports_whether_the_endpoint_was_known(bool removed, Type expected)
    {
        service.UnsubscribeAsync("https://push.example/x", Arg.Any<CancellationToken>()).Returns(removed);

        Assert.IsType(expected, await Controller.Unsubscribe("https://push.example/x", default));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(0)] // subscribed on another device only, or the push service refused
    public async Task Test_reports_how_many_subscriptions_took_the_notification(int delivered)
    {
        service.SendTestAsync(Arg.Any<CancellationToken>()).Returns(delivered);

        var result = await Controller.SendTest(default);

        Assert.Equal(delivered, result.Value?.Delivered);
    }
}
