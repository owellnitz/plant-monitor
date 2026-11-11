using System.Text.Json;
using PlantMonitor.Backend.Dtos;
using PlantMonitor.Backend.Repositories;

namespace PlantMonitor.Backend.Services;

public interface IPushService
{
    /// <summary>Null when push is not configured.</summary>
    string? PublicKey { get; }

    Task SubscribeAsync(PushSubscriptionInput input, CancellationToken ct);
    Task<bool> UnsubscribeAsync(string endpoint, CancellationToken ct);

    /// <summary>Fans one plant's new state out to every subscribed browser.</summary>
    Task NotifyAsync(Plant plant, WaterStatus status, int percent, CancellationToken ct);

    /// <summary>
    /// Tells subscribed browsers that a plant's sensor has stopped reporting: all of them,
    /// or just the endpoints in <paramref name="only"/>. Returns the endpoints still owed
    /// the alert because their send failed; null means everyone, as in <paramref name="only"/>.
    /// </summary>
    Task<IReadOnlyCollection<string>?> NotifyOfflineAsync(
        Plant plant, TimeSpan silentFor, IReadOnlyCollection<string>? only, CancellationToken ct);

    /// <summary>
    /// Sends a fixed notification to one subscription; null when the endpoint is unknown.
    /// Notifications otherwise only fire on a threshold crossing, so without this a setup
    /// that never worked is indistinguishable from one where no plant has crossed yet —
    /// which on iOS is the common case.
    /// </summary>
    Task<PushSendResult?> SendTestAsync(string endpoint, CancellationToken ct);
}

public sealed class PushService(
    IPushSubscriptionRepository subscriptions,
    IPushSender sender,
    ILogger<PushService> log) : IPushService
{
    public string? PublicKey => sender.PublicKey;

    public Task SubscribeAsync(PushSubscriptionInput input, CancellationToken ct) =>
        subscriptions.UpsertAsync(new PushSubscriptionRow
        {
            Endpoint = input.Endpoint,
            P256dh = input.P256dh,
            Auth = input.Auth,
        }, ct);

    public Task<bool> UnsubscribeAsync(string endpoint, CancellationToken ct) =>
        subscriptions.DeleteByEndpointAsync(endpoint, ct);

    public async Task NotifyAsync(Plant plant, WaterStatus status, int percent, CancellationToken ct)
    {
        // Self-contained text: the app is LAN-only, so tapping it away from home loads nothing.
        var (title, body) = status switch
        {
            WaterStatus.Must => ($"{plant.Name} needs water",
                $"{percent}% — must water below {plant.MustWaterPercent}%"),
            _ => ($"{plant.Name} could use water",
                $"{percent}% — can water below {plant.CanWaterPercent}%"),
        };

        try
        {
            var (delivered, _) = await FanOutAsync(
                // The tag collapses repeat notifications for the same plant on the device.
                BuildPayload(title, body, $"plant-{plant.Id}", $"/plant/{plant.Id}"), ct);

            log.LogInformation("Notified {Count} subscriber(s): {Plant} is now {Status} at {Percent}%",
                delivered, plant.Name, status, percent);
        }
        catch (Exception ex)
        {
            // The reading is already stored; a push failure must not fail ingest.
            log.LogError(ex, "Push notification for {Plant} failed", plant.Name);
        }
    }

    public async Task<IReadOnlyCollection<string>?> NotifyOfflineAsync(
        Plant plant, TimeSpan silentFor, IReadOnlyCollection<string>? only, CancellationToken ct)
    {
        var hours = (int)silentFor.TotalHours;
        try
        {
            var (delivered, failed) = await FanOutAsync(BuildPayload($"{plant.Name} sensor offline",
                $"No reading for {hours} h — the device may need a restart",
                $"sensor-{plant.DeviceId}", $"/sensor/{Uri.EscapeDataString(plant.DeviceId!)}"), ct, only);

            if (delivered > 0)
                log.LogInformation("Notified {Count} subscriber(s): sensor {DeviceId} of {Plant} silent for {Hours} h",
                    delivered, plant.DeviceId, plant.Name, hours);
            return failed;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Offline notification for {Plant} failed", plant.Name);
            return only;
        }
    }

    public async Task<PushSendResult?> SendTestAsync(string endpoint, CancellationToken ct)
    {
        var target = (await subscriptions.GetAllAsync(ct)).FirstOrDefault(s => s.Endpoint == endpoint);
        if (target is null) return null;

        var result = await sender.SendAsync(target,
            BuildPayload("Plant Monitor", "Notifications are working.", "push-test", "/settings"), ct);
        if (result == PushSendResult.Gone)
            await subscriptions.DeleteByEndpointAsync(endpoint, ct);
        return result;
    }

    /// <summary>
    /// Sends one payload to every subscription, or just to the endpoints in <paramref name="only"/>;
    /// returns how many took it and the endpoints whose send failed.
    /// </summary>
    private async Task<(int Delivered, List<string> Failed)> FanOutAsync(
        string payload, CancellationToken ct, IReadOnlyCollection<string>? only = null)
    {
        var delivered = 0;
        List<string> failed = [];

        foreach (var target in await subscriptions.GetAllAsync(ct))
        {
            if (only?.Contains(target.Endpoint) == false) continue;

            var result = await sender.SendAsync(target, payload, ct);
            if (result == PushSendResult.Delivered) delivered++;
            if (result == PushSendResult.Failed) failed.Add(target.Endpoint);

            if (result == PushSendResult.Gone)
            {
                await subscriptions.DeleteByEndpointAsync(target.Endpoint, ct);
                log.LogInformation("Removed expired push subscription {Endpoint}", target.Endpoint);
            }
        }

        return (delivered, failed);
    }

    /// <summary>The shape ngsw-worker.js expects; it handles the click itself.</summary>
    private static string BuildPayload(string title, string body, string tag, string url) =>
        JsonSerializer.Serialize(new
        {
            notification = new
            {
                title,
                body,
                icon = "/icons/icon-192x192.png",
                badge = "/icons/icon-96x96.png",
                tag,
                // Re-alert on can -> must even while the earlier one is unread.
                renotify = true,
                data = new
                {
                    onActionClick = new
                    {
                        @default = new { operation = "navigateLastFocusedOrOpen", url },
                    },
                },
            },
        });
}
