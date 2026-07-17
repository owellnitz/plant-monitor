using PlantMonitor.Backend.Repositories;

namespace PlantMonitor.Backend.Services;

public interface IReadingService
{
    Task<bool> RecordAsync(Reading reading, CancellationToken ct);
    Task<IReadOnlyList<ReadingRow>> GetReadingsAsync(string? deviceId, DateTimeOffset? since, int limit, CancellationToken ct);
}

public sealed class ReadingService(IReadingRepository readings) : IReadingService
{
    /// <summary>
    /// Devices publish once per hourly wake cycle; a second reading arriving
    /// sooner is a replay from an unexpected device reboot (brownout, manual
    /// reset, flashing) and would show up as a duplicate.
    /// </summary>
    private static readonly TimeSpan DedupWindow = TimeSpan.FromMinutes(5);

    /// <summary>Returns false when the reading was dropped as a duplicate.</summary>
    public async Task<bool> RecordAsync(Reading reading, CancellationToken ct)
    {
        var latest = await readings.GetLatestForDeviceAsync(reading.Id, ct);
        if (latest is not null && DateTimeOffset.UtcNow - latest.ReceivedAt < DedupWindow)
            return false;

        await readings.AddAsync(new ReadingRow
        {
            DeviceId = reading.Id,
            Raw = reading.Raw,
            Percent = reading.Percent,
        }, ct);
        return true;
    }

    public Task<IReadOnlyList<ReadingRow>> GetReadingsAsync(
        string? deviceId, DateTimeOffset? since, int limit, CancellationToken ct) =>
        readings.GetReadingsAsync(deviceId, since, Math.Clamp(limit, 1, 500), ct);
}
