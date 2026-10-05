using Microsoft.EntityFrameworkCore;

namespace PlantMonitor.Backend.Repositories;

public interface IPlantRepository
{
    Task<IReadOnlyList<Plant>> GetAllAsync(CancellationToken ct);
    Task<Plant?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Plant?> GetByDeviceIdAsync(string deviceId, CancellationToken ct);
    Task AddAsync(Plant plant, CancellationToken ct);
    Task UpdateAsync(Plant plant, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);
    Task<bool> DeviceTakenAsync(string deviceId, Guid? excludingId, CancellationToken ct);
}

public sealed class PlantRepository(AppDbContext db) : IPlantRepository
{
    public async Task<IReadOnlyList<Plant>> GetAllAsync(CancellationToken ct) =>
        await db.Plants.Include(p => p.Species).OrderBy(p => p.Name).ToListAsync(ct);

    public Task<Plant?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Plants.Include(p => p.Species).FirstOrDefaultAsync(p => p.Id == id, ct);

    /// <summary>
    /// Tracked, so the caller can write back the plant's notified status through
    /// <see cref="UpdateAsync"/>. device_id is unique, so this is at most one row.
    /// </summary>
    public Task<Plant?> GetByDeviceIdAsync(string deviceId, CancellationToken ct) =>
        db.Plants.FirstOrDefaultAsync(p => p.DeviceId == deviceId, ct);

    public async Task AddAsync(Plant plant, CancellationToken ct)
    {
        db.Plants.Add(plant);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Plant plant, CancellationToken ct) =>
        await db.SaveChangesAsync(ct);

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct) =>
        await db.Plants.Where(p => p.Id == id).ExecuteDeleteAsync(ct) > 0;

    public Task<bool> DeviceTakenAsync(string deviceId, Guid? excludingId, CancellationToken ct) =>
        db.Plants.AnyAsync(p => p.DeviceId == deviceId && p.Id != excludingId, ct);
}
