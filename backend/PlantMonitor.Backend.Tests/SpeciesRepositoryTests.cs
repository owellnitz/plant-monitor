using PlantMonitor.Backend.Repositories;
using Xunit;

namespace PlantMonitor.Backend.Tests;

[Collection(nameof(RepositoryCollection))]
public class SpeciesRepositoryTests(StackFixture stack) : RepositoryTestBase(stack)
{
    [Fact]
    public async Task Species_add_then_find_by_name()
    {
        await using var ctx = await MigratedContextAsync();
        var repo = new SpeciesRepository(ctx);

        var added = await repo.AddAsync("sr-fern", default);
        Assert.NotEqual(Guid.Empty, added.Id);

        var found = await repo.FindByNameAsync("sr-fern", default);
        Assert.Equal(added.Id, found!.Id);
        Assert.Null(await repo.FindByNameAsync("sr-missing", default));
    }
}
