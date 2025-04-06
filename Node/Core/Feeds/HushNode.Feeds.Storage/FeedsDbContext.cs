using HushShared.Feeds.Model;
using Microsoft.EntityFrameworkCore;

namespace HushNode.Feeds.Storage;

public class FeedsDbContext(
    FeedsDbContextConfigurator blockchainDbContextConfigurator,
    DbContextOptions<FeedsDbContext> options) : DbContext(options)
{
    private readonly FeedsDbContextConfigurator _blockchainDbContextConfigurator = blockchainDbContextConfigurator;

    public DbSet<Feed> Feeds { get; set; }
    public DbSet<FeedParticipant> FeedParticipants { get; set; }
    public DbSet<FeedMessage> FeedMessages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        this._blockchainDbContextConfigurator.Configure(modelBuilder);
    }
}
