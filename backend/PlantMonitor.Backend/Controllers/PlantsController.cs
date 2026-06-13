using Microsoft.AspNetCore.Mvc;
using PlantMonitor.Backend.Dtos;
using PlantMonitor.Backend.Services;

namespace PlantMonitor.Backend.Controllers;

[ApiController]
[Route("api/plants")]
public sealed class PlantsController(IPlantService plants) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<PlantDto>> Get(CancellationToken ct)
    {
        var all = await plants.GetPlantsAsync(ct);
        return all.Select(Map).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PlantDto>> GetById(Guid id, CancellationToken ct)
    {
        var plant = await plants.GetPlantAsync(id, ct);
        return plant is null ? NotFound() : Map(plant);
    }

    [HttpPost]
    public async Task<ActionResult<PlantDto>> Create(PlantInput input, CancellationToken ct)
    {
        var result = await plants.CreateAsync(input, ct);
        return result.Status == PlantWriteStatus.DeviceConflict
            ? Conflict(DeviceConflictMessage(input.DeviceId))
            : CreatedAtAction(nameof(GetById), new { id = result.Plant!.Plant.Id }, Map(result.Plant));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PlantDto>> Update(Guid id, PlantInput input, CancellationToken ct)
    {
        var result = await plants.UpdateAsync(id, input, ct);
        return result.Status switch
        {
            PlantWriteStatus.NotFound => NotFound(),
            PlantWriteStatus.DeviceConflict => Conflict(DeviceConflictMessage(input.DeviceId)),
            _ => Map(result.Plant!),
        };
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await plants.DeleteAsync(id, ct) ? NoContent() : NotFound();

    private static PlantDto Map(PlantWithReading p) => new(
        p.Plant.Id, p.Plant.Name, p.Plant.Species?.Name, p.Plant.Location, p.Plant.SunExposure,
        p.Plant.DeviceId, p.Latest?.Percent, p.Latest?.Raw, p.Latest?.ReceivedAt);

    private static string DeviceConflictMessage(string? deviceId) =>
        $"Sensor '{deviceId}' is already assigned to a plant.";
}
