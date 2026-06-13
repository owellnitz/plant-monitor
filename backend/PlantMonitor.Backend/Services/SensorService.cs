namespace PlantMonitor.Backend;

public interface ISensorService
{
    Task<IReadOnlyList<ReadingRow>> GetLatestPerDeviceAsync(CancellationToken ct);
    Task<IReadOnlyList<ReadingRow>> GetUnassignedAsync(CancellationToken ct);
}

public sealed class SensorService(IReadingRepository readings) : ISensorService
{
    public Task<IReadOnlyList<ReadingRow>> GetLatestPerDeviceAsync(CancellationToken ct) =>
        readings.GetLatestPerDeviceAsync(ct);

    public Task<IReadOnlyList<ReadingRow>> GetUnassignedAsync(CancellationToken ct) =>
        readings.GetUnassignedLatestAsync(ct);
}
