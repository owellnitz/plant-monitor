using Microsoft.AspNetCore.Mvc;
using PlantMonitor.Backend.Dtos;
using PlantMonitor.Backend.Services;

namespace PlantMonitor.Backend.Controllers;

[ApiController]
[Route("api/sensors")]
public sealed class SensorsController(ISensorService sensors) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<SensorOverview>> GetAll(CancellationToken ct) =>
        sensors.GetAllAsync(ct);

    [HttpDelete("{deviceId}")]
    public async Task<IActionResult> Delete(string deviceId, CancellationToken ct) =>
        await sensors.DeleteAsync(deviceId, ct) switch
        {
            SensorDeleteResult.Deleted => NoContent(),
            SensorDeleteResult.Assigned => Conflict($"Sensor '{deviceId}' is assigned to a plant; unassign it first."),
            _ => NotFound(),
        };
}
