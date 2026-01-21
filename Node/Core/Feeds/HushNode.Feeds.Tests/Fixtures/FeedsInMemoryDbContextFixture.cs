using Microsoft.EntityFrameworkCore;
using HushNode.Feeds.Storage;

namespace HushNode.Feeds.Tests.Fixtures;

/// <summary>
/// Fixture for creating in-memory database contexts for Feeds repository testing.
/// </summary>
public class FeedsInMemoryDbContextFixture : IDisposable
{
    private readonly DbContextOptions<FeedsDbContext> _options;
    private readonly FeedsDbContextConfigurator _configurator;

    public FeedsInMemoryDbContextFixture()
    {
        _configurator = new FeedsDbContextConfigurator();
        _options = new DbContextOptionsBuilder<FeedsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    public FeedsDbContext CreateContext()
    {
        return new FeedsDbContext(_configurator, _options);
    }

    public void Dispose()
    {
        // In-memory database is cleaned up automatically
    }
}
