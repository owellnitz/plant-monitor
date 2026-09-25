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
        services.AddScoped<IPushSender, WebPushSender>();
        services.AddScoped<IPushService, PushService>();

        // FirmwareFetchWorker resolves IHttpClientFactory; WebPushSender resolves
        // PushServiceClient as a typed client. AddHttpClient<T> registers the
        // factory too, but both are spelled out so dropping either consumer
        // cannot quietly break the other.
        services.AddHttpClient();
        services.AddHttpClient<PushServiceClient>();

        return services;
    }
}
