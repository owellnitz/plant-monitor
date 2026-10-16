using PlantMonitor.Backend.Repositories;
using Xunit;

namespace PlantMonitor.Backend.Tests;

[Collection(nameof(RepositoryCollection))]
public class PushSubscriptionRepositoryTests(StackFixture stack) : RepositoryTestBase(stack)
{
    private static PushSubscriptionRow Subscription(string endpoint, string p256dh, string auth) =>
        new() { Endpoint = endpoint, P256dh = p256dh, Auth = auth };

    [Fact]
    public async Task Push_upsert_rotates_the_keys_of_a_known_endpoint()
    {
        await using var ctx = await MigratedContextAsync();
        var repo = new PushSubscriptionRepository(ctx);
        var endpoint = $"https://push.example/{Guid.NewGuid()}";
        await repo.UpsertAsync(Subscription(endpoint, "key-1", "auth-1"), default);

        await repo.UpsertAsync(Subscription(endpoint, "key-2", "auth-2"), default);

        var stored = Assert.Single(await repo.GetAllAsync(default), s => s.Endpoint == endpoint);
        Assert.Equal("key-2", stored.P256dh);
        Assert.Equal("auth-2", stored.Auth);
    }

    [Fact]
    public async Task Push_concurrent_upserts_of_a_new_endpoint_store_one_row()
    {
        await using var ctx1 = await MigratedContextAsync();
        await using var ctx2 = NewContext();
        var endpoint = $"https://push.example/{Guid.NewGuid()}";

        await Task.WhenAll(
            new PushSubscriptionRepository(ctx1).UpsertAsync(Subscription(endpoint, "k", "a"), default),
            new PushSubscriptionRepository(ctx2).UpsertAsync(Subscription(endpoint, "k", "a"), default));

        Assert.Single(await new PushSubscriptionRepository(ctx1).GetAllAsync(default), s => s.Endpoint == endpoint);
    }

    [Fact]
    public async Task Push_delete_removes_only_the_named_endpoint()
    {
        await using var ctx = await MigratedContextAsync();
        var repo = new PushSubscriptionRepository(ctx);
        var gone = $"https://push.example/{Guid.NewGuid()}";
        var kept = $"https://push.example/{Guid.NewGuid()}";
        await repo.UpsertAsync(Subscription(gone, "k", "a"), default);
        await repo.UpsertAsync(Subscription(kept, "k", "a"), default);

        Assert.True(await repo.DeleteByEndpointAsync(gone, default));

        var all = await repo.GetAllAsync(default);
        Assert.DoesNotContain(all, s => s.Endpoint == gone);
        Assert.Contains(all, s => s.Endpoint == kept);
        Assert.False(await repo.DeleteByEndpointAsync(gone, default));
    }
}
