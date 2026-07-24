using PlantMonitor.Backend.Dtos;
using PlantMonitor.Backend.Repositories;

namespace PlantMonitor.Backend.Services;

public interface ISensorService
{
    Task<IReadOnlyList<SensorOverview>> GetAllAsync(CancellationToken ct);
    Task<IReadOnlyList<ReadingRow>> GetUnassignedAsync(CancellationToken ct);
}

public sealed class SensorService(IReadingRepository readings, IPlantRepository plants) : ISensorService
{
    public async Task<IReadOnlyList<SensorOverview>> GetAllAsync(CancellationToken ct)
    {
        var latest = await readings.GetAllLatestAsync(ct);
        var byDevice = (await plants.GetAllAsync(ct))
            .Where(p => p.DeviceId != null)
            .ToDictionary(p => p.DeviceId!);
        return latest.Select(r =>
        {
            byDevice.TryGetValue(r.DeviceId, out var plant);
            return new SensorOverview(r.DeviceId, r.Raw, r.Percent, r.ReceivedAt, r.Fw,
                plant?.Id, plant?.Name);
        }).ToList();
    }

    public Task<IReadOnlyList<ReadingRow>> GetUnassignedAsync(CancellationToken ct) =>
        readings.GetUnassignedLatestAsync(ct);
}
