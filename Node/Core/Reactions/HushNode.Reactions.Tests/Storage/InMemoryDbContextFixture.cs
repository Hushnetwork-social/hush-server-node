using Microsoft.EntityFrameworkCore;
using HushNode.Reactions.Storage;

namespace HushNode.Reactions.Tests.Storage;

/// <summary>
/// Fixture for creating in-memory database contexts for testing.
/// </summary>
public class InMemoryDbContextFixture : IDisposable
{
    private readonly DbContextOptions<ReactionsDbContext> _options;
    private readonly ReactionsDbContextConfigurator _configurator;

    public InMemoryDbContextFixture()
    {
        _configurator = new ReactionsDbContextConfigurator();
        _options = new DbContextOptionsBuilder<ReactionsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    public ReactionsDbContext CreateContext()
    {
        return new ReactionsDbContext(_configurator, _options);
    }

    public void Dispose()
    {
        // In-memory database is cleaned up automatically
    }
}
