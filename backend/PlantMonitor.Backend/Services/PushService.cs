using System.Text.Json;
using PlantMonitor.Backend.Repositories;

namespace PlantMonitor.Backend.Services;

public interface IPushService
{
    /// <summary>Fans one plant's new state out to every subscribed browser.</summary>
    Task NotifyAsync(Plant plant, WaterStatus status, int percent, CancellationToken ct);
}

public sealed class PushService(
    IPushSubscriptionRepository subscriptions,
    IPushSender sender,
    ILogger<PushService> log) : IPushService
{
    public async Task NotifyAsync(Plant plant, WaterStatus status, int percent, CancellationToken ct)
    {
        // Title and body carry the plant name and the actual reading on purpose:
        // the app is only reachable on the home LAN, so tapping the notification
        // while away cannot load anything.
        var (title, body) = status switch
        {
            WaterStatus.Must => ($"{plant.Name} needs water",
                $"{percent}% — must water below {plant.MustWaterPercent}%"),
            _ => ($"{plant.Name} could use water",
                $"{percent}% — can water below {plant.CanWaterPercent}%"),
        };

        var delivered = await FanOutAsync(
            // The tag collapses repeat notifications for the same plant on the device.
            BuildPayload(title, body, $"plant-{plant.Id}", $"/plant/{plant.Id}"), ct);

        log.LogInformation("Notified {Count} subscriber(s): {Plant} is now {Status} at {Percent}%",
            delivered, plant.Name, status, percent);
    }

    /// <summary>Sends one payload to every subscription; returns how many took it.</summary>
    private async Task<int> FanOutAsync(string payload, CancellationToken ct)
    {
        var delivered = 0;

        foreach (var target in await subscriptions.GetAllAsync(ct))
        {
            var result = await sender.SendAsync(target, payload, ct);
            if (result == PushSendResult.Delivered) delivered++;

            // The browser dropped the subscription (app deleted, permission
            // revoked); the endpoint will never work again.
            if (result == PushSendResult.Gone)
            {
                await subscriptions.DeleteByEndpointAsync(target.Endpoint, ct);
                log.LogInformation("Removed expired push subscription {Endpoint}", target.Endpoint);
            }
        }

        return delivered;
    }

    /// <summary>
    /// The shape ngsw-worker.js expects. onActionClick is handled inside the
    /// Angular service worker, so the app needs no notificationClicks code.
    /// </summary>
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
