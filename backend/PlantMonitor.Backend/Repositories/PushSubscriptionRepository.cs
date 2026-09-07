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
    /// rotates the keys — so match on the endpoint and overwrite them.
    /// </summary>
    public async Task UpsertAsync(PushSubscriptionRow subscription, CancellationToken ct)
    {
        var existing = await db.PushSubscriptions
            .FirstOrDefaultAsync(s => s.Endpoint == subscription.Endpoint, ct);

        if (existing is null)
            db.PushSubscriptions.Add(subscription);
        else
            (existing.P256dh, existing.Auth) = (subscription.P256dh, subscription.Auth);

        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteByEndpointAsync(string endpoint, CancellationToken ct) =>
        await db.PushSubscriptions.Where(s => s.Endpoint == endpoint).ExecuteDeleteAsync(ct) > 0;
}
