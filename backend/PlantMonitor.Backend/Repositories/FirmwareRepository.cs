using Microsoft.EntityFrameworkCore;
using PlantMonitor.Backend.Dtos;

namespace PlantMonitor.Backend.Repositories;

public interface IFirmwareRepository
{
    Task<bool> ExistsAsync(string version, CancellationToken ct);
    Task AddAsync(FirmwareImage image, CancellationToken ct);
    Task<FirmwareInfo?> GetLatestAsync(CancellationToken ct);
    Task<byte[]?> GetDataAsync(string version, CancellationToken ct);
}

public sealed class FirmwareRepository(AppDbContext db) : IFirmwareRepository
{
    public Task<bool> ExistsAsync(string version, CancellationToken ct) =>
        db.FirmwareImages.AnyAsync(f => f.Version == version, ct);

    public async Task AddAsync(FirmwareImage image, CancellationToken ct)
    {
        db.FirmwareImages.Add(image);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The most recently fetched image. Images are only ever inserted when the
    /// poller sees a release it has not cached, so fetch order is release order
    /// — no version-string parsing needed to pick the newest.
    /// </summary>
    public Task<FirmwareInfo?> GetLatestAsync(CancellationToken ct) =>
        db.FirmwareImages
            .OrderByDescending(f => f.FetchedAt)
            .Select(f => new FirmwareInfo(f.Version, f.Size, f.Sha256))
            .FirstOrDefaultAsync(ct);

    public Task<byte[]?> GetDataAsync(string version, CancellationToken ct) =>
        db.FirmwareImages.Where(f => f.Version == version)
            .Select(f => f.Data)
            .FirstOrDefaultAsync(ct);
}
