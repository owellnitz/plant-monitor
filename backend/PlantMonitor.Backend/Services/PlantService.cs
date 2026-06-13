using PlantMonitor.Backend.Dtos;
using PlantMonitor.Backend.Repositories;

namespace PlantMonitor.Backend.Services;

/// <summary>A plant paired with its latest reading (null when no sensor/readings).</summary>
public sealed record PlantWithReading(Plant Plant, ReadingRow? Latest);

public enum PlantWriteStatus { Ok, NotFound, DeviceConflict }

public sealed record PlantWriteResult(PlantWriteStatus Status, PlantWithReading? Plant);

public interface IPlantService
{
    Task<IReadOnlyList<PlantWithReading>> GetPlantsAsync(CancellationToken ct);
    Task<PlantWithReading?> GetPlantAsync(Guid id, CancellationToken ct);
    Task<PlantWriteResult> CreateAsync(PlantInput input, CancellationToken ct);
    Task<PlantWriteResult> UpdateAsync(Guid id, PlantInput input, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);
}

public sealed class PlantService(
    IPlantRepository plants,
    ISpeciesRepository species,
    IReadingRepository readings) : IPlantService
{
    public async Task<IReadOnlyList<PlantWithReading>> GetPlantsAsync(CancellationToken ct)
    {
        var all = await plants.GetAllAsync(ct);
        var deviceIds = all.Where(p => p.DeviceId is not null).Select(p => p.DeviceId!).ToList();
        var latest = await readings.GetLatestForDevicesAsync(deviceIds, ct);
        var byDevice = latest.ToDictionary(r => r.DeviceId);
        return all.Select(p => new PlantWithReading(p, LatestFor(p, byDevice))).ToList();
    }

    public async Task<PlantWithReading?> GetPlantAsync(Guid id, CancellationToken ct)
    {
        var plant = await plants.GetByIdAsync(id, ct);
        return plant is null ? null : new PlantWithReading(plant, await LatestFor(plant, ct));
    }

    public async Task<PlantWriteResult> CreateAsync(PlantInput input, CancellationToken ct)
    {
        var deviceId = Trim(input.DeviceId);
        if (deviceId is not null && await plants.DeviceTakenAsync(deviceId, null, ct))
            return new PlantWriteResult(PlantWriteStatus.DeviceConflict, null);

        var plant = new Plant
        {
            Name = input.Name,
            SpeciesId = await ResolveSpeciesAsync(input.SpeciesName, ct),
            Location = Trim(input.Location),
            SunExposure = Trim(input.SunExposure),
            DeviceId = deviceId,
        };
        await plants.AddAsync(plant, ct);
        return new PlantWriteResult(PlantWriteStatus.Ok, await GetPlantAsync(plant.Id, ct));
    }

    public async Task<PlantWriteResult> UpdateAsync(Guid id, PlantInput input, CancellationToken ct)
    {
        var plant = await plants.GetByIdAsync(id, ct);
        if (plant is null)
            return new PlantWriteResult(PlantWriteStatus.NotFound, null);

        var deviceId = Trim(input.DeviceId);
        if (deviceId is not null && await plants.DeviceTakenAsync(deviceId, id, ct))
            return new PlantWriteResult(PlantWriteStatus.DeviceConflict, null);

        plant.Name = input.Name;
        plant.SpeciesId = await ResolveSpeciesAsync(input.SpeciesName, ct);
        plant.Location = Trim(input.Location);
        plant.SunExposure = Trim(input.SunExposure);
        plant.DeviceId = deviceId;
        await plants.UpdateAsync(plant, ct);

        return new PlantWriteResult(PlantWriteStatus.Ok, new PlantWithReading(plant, await LatestFor(plant, ct)));
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct) =>
        plants.DeleteAsync(id, ct);

    /// <summary>Finds or creates a species by name; null/blank clears the species.</summary>
    private async Task<Guid?> ResolveSpeciesAsync(string? name, CancellationToken ct)
    {
        name = Trim(name);
        if (name is null)
            return null;
        var existing = await species.FindByNameAsync(name, ct);
        return existing?.Id ?? (await species.AddAsync(name, ct)).Id;
    }

    private async Task<ReadingRow?> LatestFor(Plant plant, CancellationToken ct) =>
        plant.DeviceId is null ? null : await readings.GetLatestForDeviceAsync(plant.DeviceId, ct);

    private static ReadingRow? LatestFor(Plant plant, IReadOnlyDictionary<string, ReadingRow> byDevice) =>
        plant.DeviceId is not null && byDevice.TryGetValue(plant.DeviceId, out var r) ? r : null;

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
