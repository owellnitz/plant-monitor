using PlantMonitor.Backend.Dtos;
using PlantMonitor.Backend.Repositories;

namespace PlantMonitor.Backend.Services;

public interface IFirmwareService
{
    Task<FirmwareInfo?> GetUpdateAsync(string? current, CancellationToken ct);
    Task<byte[]?> GetImageAsync(string? version, CancellationToken ct);
}

public sealed class FirmwareService(IFirmwareRepository firmware) : IFirmwareService
{
    /// <summary>
    /// The cached image when it differs from what the caller is running, else
    /// null (nothing to do). Comparison is string equality: a release build's
    /// id is exactly its tag, so anything else — a dev build, an older release
    /// — counts as "not the latest" and gets offered the update.
    /// </summary>
    public async Task<FirmwareInfo?> GetUpdateAsync(string? current, CancellationToken ct)
    {
        var latest = await firmware.GetLatestAsync(ct);
        return latest is null || latest.Version == current ? null : latest;
    }

    /// <summary>
    /// The image bytes for an explicit version, or the latest when none is
    /// asked for. Devices pass the version they were offered: a release landing
    /// between the update check and the download would otherwise hand them
    /// bytes that do not match the SHA-256 they are verifying against.
    /// </summary>
    public async Task<byte[]?> GetImageAsync(string? version, CancellationToken ct)
    {
        version ??= (await firmware.GetLatestAsync(ct))?.Version;
        return version is null ? null : await firmware.GetDataAsync(version, ct);
    }
}
