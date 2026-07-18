namespace PlantMonitor.Backend;

/// <summary>
/// The release version baked into the container image as the APP_VERSION
/// environment variable (see release.yml); dev runs fall back.
/// </summary>
public static class AppVersion
{
    public static string Resolve(IConfiguration config) => config["APP_VERSION"] ?? "0.0.0-dev";
}
