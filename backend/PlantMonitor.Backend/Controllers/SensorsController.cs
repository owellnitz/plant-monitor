using Microsoft.AspNetCore.Mvc;
using PlantMonitor.Backend.Dtos;
using PlantMonitor.Backend.Services;

namespace PlantMonitor.Backend.Controllers;

[ApiController]
[Route("api/sensors")]
public sealed class SensorsController(ISensorService sensors) : ControllerBase
{
    [HttpGet("unassigned")]
    public async Task<IReadOnlyList<Sensor>> GetUnassigned(CancellationToken ct) =>
        Map(await sensors.GetUnassignedAsync(ct));

    private static List<Sensor> Map(IReadOnlyList<ReadingRow> readings) =>
        readings.Select(r => new Sensor(r.DeviceId, r.Raw, r.Percent, r.ReceivedAt)).ToList();
}
