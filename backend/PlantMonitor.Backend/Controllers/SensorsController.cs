using Microsoft.AspNetCore.Mvc;

namespace PlantMonitor.Backend;

[ApiController]
[Route("api/sensors")]
public sealed class SensorsController(ISensorService sensors) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<Sensor>> Get(CancellationToken ct) =>
        Map(await sensors.GetLatestPerDeviceAsync(ct));

    [HttpGet("unassigned")]
    public async Task<IReadOnlyList<Sensor>> GetUnassigned(CancellationToken ct) =>
        Map(await sensors.GetUnassignedAsync(ct));

    private static List<Sensor> Map(IReadOnlyList<ReadingRow> readings) =>
        readings.Select(r => new Sensor(r.DeviceId, r.Raw, r.Percent, r.ReceivedAt)).ToList();
}
