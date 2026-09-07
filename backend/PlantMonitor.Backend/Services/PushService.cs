using System.Text.Json;
using PlantMonitor.Backend.Dtos;
using PlantMonitor.Backend.Repositories;

namespace PlantMonitor.Backend.Services;

public interface IPushService
{
    /// <summary>Null when push is not configured; the frontend then hides the toggle.</summary>
    string? PublicKey { get; }

    Task SubscribeAsync(PushSubscriptionInput input, CancellationToken ct);
    Task<bool> UnsubscribeAsync(string endpoint, CancellationToken ct);

    /// <summary>Fans one plant's new state out to every subscribed browser.</summary>
    Task NotifyAsync(Plant plant, WaterStatus status, int percent, CancellationToken ct);
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
        var targets = await subscriptions.GetAllAsync(ct);
        if (targets.Count == 0) return;

        var payload = BuildPayload(plant, status, percent);

        foreach (var target in targets)
        {
            var result = await sender.SendAsync(target, payload, ct);

            // The browser dropped the subscription (app deleted, permission
            // revoked); the endpoint will never work again.
            if (result == PushSendResult.Gone)
            {
                await subscriptions.DeleteByEndpointAsync(target.Endpoint, ct);
                log.LogInformation("Removed expired push subscription {Endpoint}", target.Endpoint);
            }
        }

        log.LogInformation("Notified {Count} subscriber(s): {Plant} is now {Status} at {Percent}%",
            targets.Count, plant.Name, status, percent);
    }

    /// <summary>
    /// The shape ngsw-worker.js expects. onActionClick is handled inside the
    /// Angular service worker, so the app needs no notificationClicks code.
    ///
    /// Title and body carry the plant name and the actual reading on purpose:
    /// the app is only reachable on the home LAN, so tapping the notification
    /// while away cannot load anything.
    /// </summary>
    private static string BuildPayload(Plant plant, WaterStatus status, int percent)
    {
        var (title, body) = status switch
        {
            WaterStatus.Must => ($"{plant.Name} needs water",
                $"{percent}% — must water below {plant.MustWaterPercent}%"),
            _ => ($"{plant.Name} could use water",
                $"{percent}% — can water below {plant.CanWaterPercent}%"),
        };

        return JsonSerializer.Serialize(new
        {
            notification = new
            {
                title,
                body,
                icon = "/icons/icon-192x192.png",
                badge = "/icons/icon-96x96.png",
                // Collapses repeat notifications for the same plant on the device.
                tag = $"plant-{plant.Id}",
                data = new
                {
                    onActionClick = new
                    {
                        @default = new
                        {
                            operation = "navigateLastFocusedOrOpen",
                            url = $"/plant/{plant.Id}",
                        },
                    },
                },
            },
        });
    }
}
