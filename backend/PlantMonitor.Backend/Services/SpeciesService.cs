using PlantMonitor.Backend.Repositories;

namespace PlantMonitor.Backend.Services;

public interface ISpeciesService
{
    Task<IReadOnlyList<Species>> GetAllAsync(CancellationToken ct);
}

public sealed class SpeciesService(ISpeciesRepository species) : ISpeciesService
{
    public Task<IReadOnlyList<Species>> GetAllAsync(CancellationToken ct) =>
        species.GetAllAsync(ct);
}
