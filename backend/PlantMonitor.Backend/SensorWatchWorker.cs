using System.Globalization;
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

    // Sensors report hourly, so anything much shorter would alert on healthy ones.
    private const double MinHours = 1.5, DefaultHours = 3, MaxHours = 24 * 365;

    private readonly TimeSpan threshold = TimeSpan.FromHours(ReadHours(config["Push:OfflineHours"], log));

    /// <summary>
    /// Plant/device pairs reported for their current outage, with the push endpoints still owed
    /// the alert: empty once everyone has it, null when no send got through at all.
    /// A fresh reading re-arms the pair.
    /// Keyed by plant too, so a silent sensor moved to another plant is reported for that plant.
    /// In memory only, so a backend restart reports a still-silent sensor once more.
    /// </summary>
    private readonly Dictionary<(Guid PlantId, string DeviceId), IReadOnlyCollection<string>?> owed = [];

    /// <summary>Hours may be fractional; an unusable value must not stop the backend, so it falls back.</summary>
    private static double ReadHours(string? configured, ILogger log)
    {
        if (string.IsNullOrWhiteSpace(configured)) return DefaultHours;
        if (double.TryParse(configured, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours)
            && hours is >= MinHours and <= MaxHours)
            return hours;

        log.LogWarning("Push:OfflineHours '{Value}' is not a number of hours from {Min} to {Max}; using {Default}",
            configured, MinHours, MaxHours, DefaultHours);
        return DefaultHours;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;

        while (!ct.IsCancellationRequested)
        {
            // Right after a fresh deploy the tables may not exist yet; the next tick retries.
            try
            {
                await CheckAsync(DateTimeOffset.UtcNow, startedAt, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning("Sensor offline check failed: {Message}", ex.Message);
            }

            await Task.Delay(Interval, ct);
        }
    }

    /// <param name="startedAt">
    /// When the backend came up. While it was down no reading could be stored, so silence
    /// only counts from here; otherwise every sensor would look offline after a long downtime.
    /// </param>
    public async Task CheckAsync(DateTimeOffset now, DateTimeOffset startedAt, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var plants = scope.ServiceProvider.GetRequiredService<IPlantRepository>();
        var readings = scope.ServiceProvider.GetRequiredService<IReadingRepository>();
        var push = scope.ServiceProvider.GetRequiredService<IPushService>();
        if (push.PublicKey is null) return;

        var byDevice = (await plants.GetAllAsync(ct))
            .Where(p => p.DeviceId != null)
            .ToDictionary(p => p.DeviceId!);

        // Forget pairs whose plant was deleted or lost its sensor.
        var assigned = byDevice.Values.Select(p => (p.Id, p.DeviceId!)).ToHashSet();
        foreach (var key in owed.Keys.Where(k => !assigned.Contains(k)).ToList())
            owed.Remove(key);

        // A sensor that never reported has no reading here and so is skipped.
        foreach (var latest in await readings.GetLatestForDevicesAsync(byDevice.Keys, ct))
        {
            var plant = byDevice[latest.DeviceId];
            var key = (plant.Id, latest.DeviceId);
            var watchedSince = latest.ReceivedAt > startedAt ? latest.ReceivedAt : startedAt;
            if (now - watchedSince < threshold)
                owed.Remove(key);
            // A failed send retries next tick, for the endpoints that missed it only.
            else if (!owed.TryGetValue(key, out var only) || only is not { Count: 0 })
                owed[key] = await push.NotifyOfflineAsync(plant, now - latest.ReceivedAt, only, ct);
        }
    }
}
