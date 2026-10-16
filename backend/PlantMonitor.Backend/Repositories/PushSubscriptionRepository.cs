using Microsoft.EntityFrameworkCore;

namespace PlantMonitor.Backend.Repositories;

public interface IPushSubscriptionRepository
{
    Task<IReadOnlyList<PushSubscriptionRow>> GetAllAsync(CancellationToken ct);
    Task UpsertAsync(PushSubscriptionRow subscription, CancellationToken ct);
    Task<bool> DeleteByEndpointAsync(string endpoint, CancellationToken ct);
}

public sealed class PushSubscriptionRepository(AppDbContext db) : IPushSubscriptionRepository
{
    public async Task<IReadOnlyList<PushSubscriptionRow>> GetAllAsync(CancellationToken ct) =>
        await db.PushSubscriptions.ToListAsync(ct);

    /// <summary>
    /// A browser hands out the same endpoint again when it re-subscribes, but
    /// rotates the keys — so match on the endpoint and overwrite them. One
    /// statement, so two concurrent subscribes cannot both insert.
    /// </summary>
    public async Task UpsertAsync(PushSubscriptionRow subscription, CancellationToken ct) =>
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO push_subscriptions (endpoint, p256dh, auth)
            VALUES ({subscription.Endpoint}, {subscription.P256dh}, {subscription.Auth})
            ON CONFLICT (endpoint) DO UPDATE SET p256dh = excluded.p256dh, auth = excluded.auth
            """, ct);

    public async Task<bool> DeleteByEndpointAsync(string endpoint, CancellationToken ct) =>
        await db.PushSubscriptions.Where(s => s.Endpoint == endpoint).ExecuteDeleteAsync(ct) > 0;
}
