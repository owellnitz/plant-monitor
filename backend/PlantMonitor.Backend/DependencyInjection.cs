using Lib.Net.Http.WebPush;
using PlantMonitor.Backend.Repositories;
using PlantMonitor.Backend.Services;

namespace PlantMonitor.Backend;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the repository and service layers together with the HTTP
    /// clients they resolve.
    ///
    /// Program and the integration test hosts both compose the app through this
    /// one call, so a dependency registered next to it in Program instead of in
    /// here exists in production and is missing under test — which surfaces as
    /// an opaque 500 rather than a startup error. Keep registrations here.
    /// </summary>
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
        // Singleton so the VAPID keys are parsed, and a bad key logged, once.
        services.AddSingleton<IPushSender, WebPushSender>();
        services.AddScoped<IPushService, PushService>();

        services.AddHttpClient();
        // Push runs inside MQTT ingest; a hanging push service must not stall it.
        services.AddHttpClient(nameof(PushServiceClient), c => c.Timeout = TimeSpan.FromSeconds(10));

        return services;
    }
}
