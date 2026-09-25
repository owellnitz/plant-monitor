using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;

namespace PlantMonitor.Backend.Services;

public enum PushSendResult
{
    Delivered,
    /// <summary>The push service says this subscription no longer exists; drop the row.</summary>
    Gone,
    Failed,
}

/// <summary>
/// The transport half of push: VAPID signing and RFC 8291 payload encryption.
/// Split from <see cref="IPushService"/> so the "which plant, which wording,
/// which subscriptions" logic can be tested without a push service.
/// </summary>
public interface IPushSender
{
    /// <summary>The VAPID public key the browser needs, or null when push is not configured.</summary>
    string? PublicKey { get; }

    Task<PushSendResult> SendAsync(PushSubscriptionRow subscription, string payload, CancellationToken ct);
}

public sealed class WebPushSender : IPushSender
{
    /// <summary>
    /// Long enough that a phone offline overnight still gets the message, short
    /// enough that a days-stale "needs water" is not resurrected. Notifications
    /// fire once per transition, so nothing re-sends if this expires.
    /// </summary>
    private const int TimeToLiveSeconds = 24 * 60 * 60;

    private readonly PushServiceClient client;
    private readonly ILogger<WebPushSender> log;
    private readonly VapidAuthentication? vapid;

    public WebPushSender(PushServiceClient client, IConfiguration config, ILogger<WebPushSender> log)
    {
        this.client = client;
        this.log = log;

        var publicKey = config["WebPush:PublicKey"];
        var privateKey = config["WebPush:PrivateKey"];

        // Unset on the dev stack. Push then stays off end to end: the key
        // endpoint 503s, so the frontend hides the toggle and never subscribes.
        if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey))
        {
            log.LogInformation("Web Push is not configured (WebPush:PublicKey/PrivateKey unset); notifications are off.");
            return;
        }

        // Compose passes unset optional variables through as empty strings, so
        // this cannot lean on null alone.
        var subject = config["WebPush:Subject"];
        if (string.IsNullOrWhiteSpace(subject)) subject = "https://github.com/owellnitz/plant-monitor";

        PublicKey = publicKey;
        vapid = new VapidAuthentication(publicKey, privateKey) { Subject = subject };
    }

    public string? PublicKey { get; }

    public async Task<PushSendResult> SendAsync(PushSubscriptionRow subscription, string payload, CancellationToken ct)
    {
        if (vapid is null) return PushSendResult.Failed;

        var target = new PushSubscription { Endpoint = subscription.Endpoint };
        target.SetKey(PushEncryptionKeyName.P256DH, subscription.P256dh);
        target.SetKey(PushEncryptionKeyName.Auth, subscription.Auth);

        try
        {
            await client.RequestPushMessageDeliveryAsync(
                target, new PushMessage(payload) { TimeToLive = TimeToLiveSeconds }, vapid, ct);
            return PushSendResult.Delivered;
        }
        catch (PushServiceClientException ex)
            when (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone)
        {
            return PushSendResult.Gone;
        }
        catch (Exception ex)
        {
            // Never let a push service outage break MQTT ingest, which is what
            // this runs inside.
            log.LogWarning(ex, "Push delivery to {Endpoint} failed", subscription.Endpoint);
            return PushSendResult.Failed;
        }
    }
}
