namespace PlantMonitor.Backend;

/// <summary>One browser's Web Push subscription; no user model, the endpoint is the identity.</summary>
public class PushSubscriptionRow
{
    public Guid Id { get; set; }

    /// <summary>The push service URL to POST to.</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>The browser's P-256 public key, base64url — half of the payload encryption.</summary>
    public string P256dh { get; set; } = "";

    /// <summary>Base64url.</summary>
    public string Auth { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}
