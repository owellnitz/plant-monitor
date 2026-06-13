using Microsoft.AspNetCore.Mvc;

namespace PlantMonitor.Backend;

[ApiController]
[Route("api/species")]
public sealed class SpeciesController(ISpeciesService species) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<SpeciesDto>> Get(CancellationToken ct)
    {
        var all = await species.GetAllAsync(ct);
        return all.Select(s => new SpeciesDto(s.Id, s.Name)).ToList();
    }
}
