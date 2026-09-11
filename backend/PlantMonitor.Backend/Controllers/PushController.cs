using Microsoft.AspNetCore.Mvc;
using PlantMonitor.Backend.Dtos;
using PlantMonitor.Backend.Services;

namespace PlantMonitor.Backend.Controllers;

[ApiController]
[Route("api/push")]
public sealed class PushController(IPushService push) : ControllerBase
{
    /// <summary>
    /// The key the browser needs to subscribe. It is per-deployment while the
    /// Angular bundle is built once in CI, so the app reads it at runtime
    /// rather than having it baked in. 503 means push is not configured, which
    /// is how the frontend knows to hide the toggle.
    /// </summary>
    [HttpGet("vapid-key")]
    public ActionResult<VapidKeyDto> GetVapidKey() =>
        push.PublicKey is { } key
            ? new VapidKeyDto(key)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, "Web Push is not configured.");

    [HttpPost("subscriptions")]
    public async Task<IActionResult> Subscribe(PushSubscriptionInput input, CancellationToken ct)
    {
        await push.SubscribeAsync(input, ct);
        return NoContent();
    }

    /// <summary>
    /// The endpoint is a URL, so it travels as a query parameter rather than in
    /// the path — it does not survive being a route segment.
    /// </summary>
    [HttpDelete("subscriptions")]
    public async Task<IActionResult> Unsubscribe([FromQuery] string endpoint, CancellationToken ct) =>
        await push.UnsubscribeAsync(endpoint, ct) ? NoContent() : NotFound();

    /// <summary>
    /// Proves the whole chain works without waiting for a plant to dry out.
    /// Unauthenticated like every other route here, so anyone on the LAN can
    /// trigger it — same trust model as the rest of the API.
    /// </summary>
    [HttpPost("test")]
    public async Task<ActionResult<PushTestResult>> SendTest(CancellationToken ct) =>
        new PushTestResult(await push.SendTestAsync(ct));
}
