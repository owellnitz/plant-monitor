using PlantMonitor.Backend.Repositories;

namespace PlantMonitor.Backend.Services;

public interface IReadingService
{
    Task RecordAsync(Reading reading, CancellationToken ct);
    Task<IReadOnlyList<ReadingRow>> GetReadingsAsync(string? deviceId, DateTimeOffset? since, int limit, CancellationToken ct);
}

public sealed class ReadingService(IReadingRepository readings) : IReadingService
{
    public Task RecordAsync(Reading reading, CancellationToken ct) =>
        readings.AddAsync(new ReadingRow
        {
            DeviceId = reading.Id,
            Raw = reading.Raw,
            Percent = reading.Percent,
        }, ct);

    public Task<IReadOnlyList<ReadingRow>> GetReadingsAsync(
        string? deviceId, DateTimeOffset? since, int limit, CancellationToken ct) =>
        readings.GetReadingsAsync(deviceId, since, Math.Clamp(limit, 1, 500), ct);
}
