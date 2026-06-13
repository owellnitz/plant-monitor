namespace PlantMonitor.Backend;

public static class DependencyInjection
{
    /// <summary>Registers the repository and service layers.</summary>
    public static IServiceCollection AddPlantMonitor(this IServiceCollection services)
    {
        services.AddScoped<IReadingRepository, ReadingRepository>();
        services.AddScoped<IPlantRepository, PlantRepository>();
        services.AddScoped<ISpeciesRepository, SpeciesRepository>();

        services.AddScoped<IReadingService, ReadingService>();
        services.AddScoped<ISensorService, SensorService>();
        services.AddScoped<IPlantService, PlantService>();
        services.AddScoped<ISpeciesService, SpeciesService>();

        return services;
    }
}
