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

/// <summary>VAPID signing and payload encryption, split out so <see cref="IPushService"/> tests need no push service.</summary>
public interface IPushSender
{
    /// <summary>The VAPID public key the browser needs, or null when push is not configured.</summary>
    string? PublicKey { get; }

    Task<PushSendResult> SendAsync(PushSubscriptionRow subscription, string payload, CancellationToken ct);
}

public sealed class WebPushSender : IPushSender
{
    // Survives a phone offline overnight without resurrecting a days-stale alert.
    private const int TimeToLiveSeconds = 24 * 60 * 60;

    private readonly IHttpClientFactory http;
    private readonly ILogger<WebPushSender> log;
    private readonly VapidAuthentication? vapid;

    public WebPushSender(IHttpClientFactory http, IConfiguration config, ILogger<WebPushSender> log)
    {
        this.http = http;
        this.log = log;

        var publicKey = config["WebPush:PublicKey"];
        var privateKey = config["WebPush:PrivateKey"];

        // Unset on the dev stack: push stays off.
        if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey))
        {
            log.LogInformation("Web Push is not configured (WebPush:PublicKey/PrivateKey unset); notifications are off.");
            return;
        }

        // Compose passes unset variables as empty strings, not null.
        var subject = config["WebPush:Subject"];
        if (string.IsNullOrWhiteSpace(subject)) subject = "https://github.com/owellnitz/plant-monitor";

        try
        {
            vapid = new VapidAuthentication(publicKey, privateKey) { Subject = subject };
            PublicKey = publicKey;
        }
        catch (ArgumentException ex)
        {
            // Treat like unset keys: a bad key must not break ingest.
            log.LogError(ex, "Web Push keys are invalid; notifications are off.");
        }
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
            var client = new PushServiceClient(http.CreateClient(nameof(PushServiceClient)));
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
            // Never throw: this runs inside MQTT ingest.
            log.LogWarning(ex, "Push delivery to {Endpoint} failed", subscription.Endpoint);
            return PushSendResult.Failed;
        }
    }
}
