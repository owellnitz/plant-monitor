using Microsoft.AspNetCore.Mvc;
using PlantMonitor.Backend.Dtos;

namespace PlantMonitor.Backend.Controllers;

[ApiController]
[Route("api/version")]
public sealed class VersionController(IConfiguration config) : ControllerBase
{
    [HttpGet]
    public VersionDto Get() => new(AppVersion.Resolve(config));
}
