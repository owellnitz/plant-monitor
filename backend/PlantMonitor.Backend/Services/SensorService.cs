using PlantMonitor.Backend.Repositories;

namespace PlantMonitor.Backend.Services;

public interface ISensorService
{
    Task<IReadOnlyList<ReadingRow>> GetUnassignedAsync(CancellationToken ct);
}

public sealed class SensorService(IReadingRepository readings) : ISensorService
{
    public Task<IReadOnlyList<ReadingRow>> GetUnassignedAsync(CancellationToken ct) =>
        readings.GetUnassignedLatestAsync(ct);
}
