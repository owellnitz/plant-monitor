using PlantMonitor.Backend.Repositories;

namespace PlantMonitor.Backend.Services;

public interface IReadingService
{
    Task<bool> RecordAsync(Reading reading, CancellationToken ct);
    Task<IReadOnlyList<ReadingRow>> GetReadingsAsync(string? deviceId, DateTimeOffset? since, int limit, CancellationToken ct);
}

public sealed class ReadingService(
    IReadingRepository readings, IPlantRepository plants, IPushService push) : IReadingService
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
            Fw = reading.Fw,
        }, ct);

        await NotifyIfWorsenedAsync(reading, ct);
        return true;
    }

    /// <summary>
    /// Turns a stored reading into at most one push. The plant's own record of
    /// the state it was last seen in is what makes this fire once per crossing
    /// instead of on every hourly reading below a limit.
    /// </summary>
    private async Task NotifyIfWorsenedAsync(Reading reading, CancellationToken ct)
    {
        var plant = await plants.GetByDeviceIdAsync(reading.Id, ct);
        if (plant is null) return;

        var previous = plant.NotifiedStatus;
        var current = Watering.Evaluate(reading.Percent, plant.MustWaterPercent, plant.CanWaterPercent);
        if (current == previous) return;

        plant.NotifiedStatus = current;
        await plants.UpdateAsync(plant, ct);

        // Only a worsening crossing is worth a notification — recovering, or
        // losing the limits entirely, just updates the record. A plant seen for
        // the first time (previous is null) is baselined silently, which is what
        // keeps already-dry plants quiet when notifications are switched on.
        if (current is { } status && previous is { } before && status > before)
            await push.NotifyAsync(plant, status, reading.Percent, ct);
    }

    public Task<IReadOnlyList<ReadingRow>> GetReadingsAsync(
        string? deviceId, DateTimeOffset? since, int limit, CancellationToken ct) =>
        readings.GetReadingsAsync(deviceId, since, Math.Clamp(limit, 1, 500), ct);
}
