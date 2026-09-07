using Lib.Net.Http.WebPush;
using PlantMonitor.Backend.Repositories;
using PlantMonitor.Backend.Services;

namespace PlantMonitor.Backend;

public static class DependencyInjection
{
    /// <summary>Registers the repository and service layers.</summary>
    public static IServiceCollection AddPlantMonitor(this IServiceCollection services)
    {
        services.AddScoped<IReadingRepository, ReadingRepository>();
        services.AddScoped<IPlantRepository, PlantRepository>();
        services.AddScoped<ISpeciesRepository, SpeciesRepository>();
        services.AddScoped<IFirmwareRepository, FirmwareRepository>();
        services.AddScoped<IPushSubscriptionRepository, PushSubscriptionRepository>();

        services.AddScoped<IReadingService, ReadingService>();
        services.AddScoped<ISensorService, SensorService>();
        services.AddScoped<IPlantService, PlantService>();
        services.AddScoped<ISpeciesService, SpeciesService>();
        services.AddScoped<IFirmwareService, FirmwareService>();
        services.AddScoped<IPushSender, WebPushSender>();
        services.AddScoped<IPushService, PushService>();

        // WebPushSender's transport. Registered here rather than in Program so
        // this extension stays self-contained — the test hosts call only this.
        services.AddHttpClient<PushServiceClient>();

        return services;
    }
}
