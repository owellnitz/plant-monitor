using Microsoft.AspNetCore.Mvc;
using PlantMonitor.Backend.Dtos;
using PlantMonitor.Backend.Services;

namespace PlantMonitor.Backend.Controllers;

[ApiController]
[Route("api/readings")]
public sealed class ReadingsController(IReadingService readings) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<StoredReading>> Get(
        string? deviceId, DateTimeOffset? since, int limit = 50, CancellationToken ct = default)
    {
        var rows = await readings.GetReadingsAsync(deviceId, since, limit, ct);
        return rows.Select(r => new StoredReading(r.Id, r.DeviceId, r.Raw, r.Percent, r.ReceivedAt)).ToList();
    }
}
