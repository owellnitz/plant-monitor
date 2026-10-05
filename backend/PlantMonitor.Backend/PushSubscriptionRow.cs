namespace PlantMonitor.Backend;

/// <summary>
/// One browser's Web Push subscription, as handed out by the push service the
/// browser trusts (APNs on iOS, FCM on Chrome). There is no user model — the
/// endpoint URL is the identity, and every stored row gets every notification.
/// </summary>
public class PushSubscriptionRow
{
    public Guid Id { get; set; }

    /// <summary>The push service URL to POST to; unique, and the key we upsert on.</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>The browser's P-256 public key, base64url — half of the payload encryption.</summary>
    public string P256dh { get; set; } = "";

    /// <summary>The subscription's shared auth secret, base64url.</summary>
    public string Auth { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}
