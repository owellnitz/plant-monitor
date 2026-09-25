using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlantMonitor.Backend.Services;
using Xunit;

namespace PlantMonitor.Backend.Tests;

/// <summary>
/// Guards the composition root. A dependency registered in Program instead of
/// AddPlantMonitor resolves in production and is missing in the integration
/// test hosts, where it shows up as an opaque 500 rather than a startup error.
/// ValidateOnBuild turns that into a failure here, with the offending type named.
/// </summary>
public class CompositionTests
{
    [Fact]
    public void Every_registered_service_can_be_constructed()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        // Never connected to — the graph is only built, not exercised.
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql("Host=unused;Database=unused"));
        services.AddPlantMonitor();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        using var scope = provider.CreateScope();

        // Ingest's entry point, which pulls the repositories behind it.
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IReadingService>());
    }
}
