using Microsoft.EntityFrameworkCore;

namespace PlantMonitor.Backend;

public interface ISpeciesRepository
{
    Task<IReadOnlyList<Species>> GetAllAsync(CancellationToken ct);
    Task<Species?> FindByNameAsync(string name, CancellationToken ct);
    Task<Species> AddAsync(string name, CancellationToken ct);
}

public sealed class SpeciesRepository(AppDbContext db) : ISpeciesRepository
{
    public async Task<IReadOnlyList<Species>> GetAllAsync(CancellationToken ct) =>
        await db.Species.OrderBy(s => s.Name).ToListAsync(ct);

    public Task<Species?> FindByNameAsync(string name, CancellationToken ct) =>
        db.Species.FirstOrDefaultAsync(s => s.Name == name, ct);

    public async Task<Species> AddAsync(string name, CancellationToken ct)
    {
        var species = new Species { Name = name };
        db.Species.Add(species);
        await db.SaveChangesAsync(ct);
        return species;
    }
}
