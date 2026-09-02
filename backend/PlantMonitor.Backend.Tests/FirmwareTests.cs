using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using PlantMonitor.Backend.Controllers;
using PlantMonitor.Backend.Dtos;
using PlantMonitor.Backend.Repositories;
using PlantMonitor.Backend.Services;
using Xunit;

namespace PlantMonitor.Backend.Tests;

public class FirmwareServiceTests
{
    private readonly IFirmwareRepository firmware = Substitute.For<IFirmwareRepository>();
    private FirmwareService Service => new(firmware);

    private static readonly FirmwareInfo Latest = new("firmware-v0.4.0", 441264, "abc123");

    private void Cached(FirmwareInfo? info) =>
        firmware.GetLatestAsync(Arg.Any<CancellationToken>()).Returns(info);

    [Fact]
    public async Task Update_is_offered_when_the_device_runs_an_older_build()
    {
        Cached(Latest);

        Assert.Equal(Latest, await Service.GetUpdateAsync("firmware-v0.3.0", default));
    }

    [Fact]
    public async Task Update_is_offered_to_a_device_that_reports_nothing()
    {
        Cached(Latest);

        Assert.Equal(Latest, await Service.GetUpdateAsync(null, default));
    }

    [Fact]
    public async Task No_update_when_the_device_already_runs_the_cached_image()
    {
        Cached(Latest);

        Assert.Null(await Service.GetUpdateAsync("firmware-v0.4.0", default));
    }

    [Fact]
    public async Task No_update_when_nothing_is_cached_yet()
    {
        Cached(null);

        Assert.Null(await Service.GetUpdateAsync("firmware-v0.3.0", default));
    }

    // A dev build's id is a git describe string, never a bare tag, so it is
    // treated as out of date and offered the release image.
    [Fact]
    public async Task Update_is_offered_to_a_dev_build()
    {
        Cached(Latest);

        Assert.Equal(Latest, await Service.GetUpdateAsync("firmware-v0.4.0-3-gdeadbee", default));
    }

    [Fact]
    public async Task Image_without_a_version_serves_the_latest()
    {
        Cached(Latest);
        firmware.GetDataAsync("firmware-v0.4.0", Arg.Any<CancellationToken>()).Returns([1, 2, 3]);

        Assert.Equal([1, 2, 3], await Service.GetImageAsync(null, default));
    }

    // The device downloads the exact version it was offered: a release landing
    // in between must not swap the bytes out from under its SHA-256 check.
    [Fact]
    public async Task Image_with_a_version_serves_that_version()
    {
        Cached(Latest);
        firmware.GetDataAsync("firmware-v0.3.0", Arg.Any<CancellationToken>()).Returns([9]);

        Assert.Equal([9], await Service.GetImageAsync("firmware-v0.3.0", default));
        await firmware.DidNotReceive().GetDataAsync("firmware-v0.4.0", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Image_is_null_when_nothing_is_cached()
    {
        Cached(null);

        Assert.Null(await Service.GetImageAsync(null, default));
    }
}

public class FirmwareControllerTests
{
    private readonly IFirmwareService service = Substitute.For<IFirmwareService>();
    private FirmwareController Controller => new(service);

    [Fact]
    public async Task Latest_returns_the_metadata_when_an_update_exists()
    {
        var info = new FirmwareInfo("firmware-v0.4.0", 441264, "abc123");
        service.GetUpdateAsync("firmware-v0.3.0", Arg.Any<CancellationToken>()).Returns(info);

        var result = await Controller.Latest("firmware-v0.3.0", default);

        Assert.Equal(info, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task Latest_returns_204_when_up_to_date()
    {
        service.GetUpdateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns((FirmwareInfo?)null);

        Assert.IsType<NoContentResult>(await Controller.Latest("firmware-v0.4.0", default));
    }

    [Fact]
    public async Task Binary_serves_the_image_as_octet_stream()
    {
        service.GetImageAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns([1, 2, 3]);

        var result = Assert.IsType<FileContentResult>(await Controller.Binary(null, default));

        Assert.Equal("application/octet-stream", result.ContentType);
        Assert.Equal([1, 2, 3], result.FileContents);
    }

    [Fact]
    public async Task Binary_returns_404_for_an_unknown_version()
    {
        service.GetImageAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns((byte[]?)null);

        Assert.IsType<NotFoundResult>(await Controller.Binary("firmware-v9.9.9", default));
    }
}

