using Microsoft.AspNetCore.Mvc;
using PlantMonitor.Backend.Services;

namespace PlantMonitor.Backend.Controllers;

/// <summary>
/// What the device polls on its hourly wake. Plain HTTP on purpose: the no_std
/// ESP32-C3 has no TLS, so GitHub is unreachable from the device and the
/// backend proxies it from the local network.
/// </summary>
[ApiController]
[Route("api/firmware")]
public sealed class FirmwareController(IFirmwareService firmware) : ControllerBase
{
    /// <summary>
    /// 204 when the caller already runs the cached image (or nothing is cached
    /// yet), so the common case costs the device one short response and it can
    /// go back to sleep. 200 with the metadata when an update is available.
    /// </summary>
    [HttpGet("latest")]
    public async Task<IActionResult> Latest([FromQuery] string? current, CancellationToken ct) =>
        await firmware.GetUpdateAsync(current, ct) is { } info ? Ok(info) : NoContent();

    [HttpGet("binary")]
    public async Task<IActionResult> Binary([FromQuery] string? version, CancellationToken ct) =>
        await firmware.GetImageAsync(version, ct) is { } data
            ? File(data, "application/octet-stream")
            : NotFound();
}
