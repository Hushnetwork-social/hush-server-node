using HushServerNode.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HushServerNode.InternalModule.Feed.Cache;

public class CacheFeedDbContext : BaseDbContext
{
    private readonly CacheFeedDbContextConfigurator _cacheFeedDbContextConfigurator;

    public DbSet<FeedEntity> FeedEntities { get; set; }

    public DbSet<FeedMessageEntity> FeedMessages { get; set; }

    public DbSet<FeedParticipants> FeedParticipants { get; set; }

    public CacheFeedDbContext(
        CacheFeedDbContextConfigurator cacheFeedDbContextConfigurator,
        IConfiguration configuration) : base(configuration)
    {
        this._cacheFeedDbContextConfigurator = cacheFeedDbContextConfigurator;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        this._cacheFeedDbContextConfigurator.Configure(modelBuilder);
    }
}
