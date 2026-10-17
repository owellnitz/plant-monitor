using PlantMonitor.Backend.Repositories;
using PlantMonitor.Backend.Services;

namespace PlantMonitor.Backend;

/// <summary>
/// Pushes once when a plant's sensor has sent nothing for <c>Push:OfflineHours</c>.
/// A missing reading is not an event ingest could react to, so this polls instead.
/// Devices without a display give no other sign that they need a restart.
/// </summary>
public sealed class SensorWatchWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<SensorWatchWorker> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    private readonly TimeSpan threshold = TimeSpan.FromHours(config.GetValue("Push:OfflineHours", 3));

    /// <summary>
    /// Plant/device pairs already reported for their current outage; a fresh reading re-arms them.
    /// Keyed by plant too, so a silent sensor moved to another plant is reported for that plant.
    /// In memory only, so a backend restart reports a still-silent sensor once more.
    /// </summary>
    private readonly HashSet<(Guid PlantId, string DeviceId)> notified = [];

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // Right after a fresh deploy the tables may not exist yet; the next tick retries.
            try
            {
                await CheckAsync(DateTimeOffset.UtcNow, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning("Sensor offline check failed: {Message}", ex.Message);
            }

            await Task.Delay(Interval, ct);
        }
    }

    public async Task CheckAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var plants = scope.ServiceProvider.GetRequiredService<IPlantRepository>();
        var readings = scope.ServiceProvider.GetRequiredService<IReadingRepository>();
        var push = scope.ServiceProvider.GetRequiredService<IPushService>();

        var byDevice = (await plants.GetAllAsync(ct))
            .Where(p => p.DeviceId != null)
            .ToDictionary(p => p.DeviceId!);

        // Forget pairs whose plant was deleted or lost its sensor.
        notified.IntersectWith(byDevice.Values.Select(p => (p.Id, p.DeviceId!)));

        // A sensor that never reported has no reading here and so is skipped.
        foreach (var latest in await readings.GetLatestForDevicesAsync(byDevice.Keys, ct))
        {
            var plant = byDevice[latest.DeviceId];
            var key = (plant.Id, latest.DeviceId);
            var silentFor = now - latest.ReceivedAt;
            if (silentFor < threshold)
                notified.Remove(key);
            // Marked only once the push reached someone, so a failed send retries next tick.
            else if (!notified.Contains(key) && await push.NotifyOfflineAsync(plant, silentFor, ct) > 0)
                notified.Add(key);
        }
    }
}
