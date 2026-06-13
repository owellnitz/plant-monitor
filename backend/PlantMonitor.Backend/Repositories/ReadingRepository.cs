using Microsoft.EntityFrameworkCore;

namespace PlantMonitor.Backend;

public interface IReadingRepository
{
    Task AddAsync(ReadingRow reading, CancellationToken ct);
    Task<IReadOnlyList<ReadingRow>> GetUnassignedLatestAsync(CancellationToken ct);
    Task<IReadOnlyList<ReadingRow>> GetLatestForDevicesAsync(IReadOnlyCollection<string> deviceIds, CancellationToken ct);
    Task<ReadingRow?> GetLatestForDeviceAsync(string deviceId, CancellationToken ct);
    Task<IReadOnlyList<ReadingRow>> GetReadingsAsync(string? deviceId, DateTimeOffset? since, int limit, CancellationToken ct);
}

public sealed class ReadingRepository(AppDbContext db) : IReadingRepository
{
    public async Task AddAsync(ReadingRow reading, CancellationToken ct)
    {
        db.Readings.Add(reading);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ReadingRow>> GetUnassignedLatestAsync(CancellationToken ct)
    {
        var assigned = db.Plants.Where(p => p.DeviceId != null).Select(p => p.DeviceId);
        return await LatestPerDevice(db.Readings.Where(r => !assigned.Contains(r.DeviceId)))
            .OrderBy(r => r.DeviceId).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ReadingRow>> GetLatestForDevicesAsync(
        IReadOnlyCollection<string> deviceIds, CancellationToken ct)
    {
        if (deviceIds.Count == 0)
            return [];
        return await LatestPerDevice(db.Readings.Where(r => deviceIds.Contains(r.DeviceId))).ToListAsync(ct);
    }

    /// <summary>
    /// The newest reading per device, as a greatest-per-group anti-join: keep a
    /// row only when no newer reading exists for the same device. EF can't
    /// translate GroupBy().First() to entities, so this expresses it instead.
    /// </summary>
    private IQueryable<ReadingRow> LatestPerDevice(IQueryable<ReadingRow> candidates) =>
        candidates.Where(r => !db.Readings.Any(o => o.DeviceId == r.DeviceId && o.ReceivedAt > r.ReceivedAt));

    public Task<ReadingRow?> GetLatestForDeviceAsync(string deviceId, CancellationToken ct) =>
        db.Readings.Where(r => r.DeviceId == deviceId)
            .OrderByDescending(r => r.ReceivedAt).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ReadingRow>> GetReadingsAsync(
        string? deviceId, DateTimeOffset? since, int limit, CancellationToken ct)
    {
        var query = db.Readings.AsQueryable();
        if (deviceId is not null)
            query = query.Where(r => r.DeviceId == deviceId);
        if (since is not null)
            query = query.Where(r => r.ReceivedAt >= since);
        return await query.OrderByDescending(r => r.ReceivedAt).Take(limit).ToListAsync(ct);
    }
}
