using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PlantMonitor.Backend.Tests;

/// <summary>
/// Repository tests against a real postgres container — the LINQ they run
/// (the latest-per-device anti-join in particular) only proves out against
/// the actual provider.
/// </summary>
public abstract class RepositoryTestBase(StackFixture stack)
{
    protected AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(stack.Db.GetConnectionString()).Options;
        return new AppDbContext(options);
    }

    protected async Task<AppDbContext> MigratedContextAsync()
    {
        var ctx = NewContext();
        await ctx.Database.MigrateAsync();
        return ctx;
    }
}

// One shared container for all repository test classes instead of one each.
[CollectionDefinition(nameof(RepositoryCollection))]
public class RepositoryCollection : ICollectionFixture<StackFixture>;
