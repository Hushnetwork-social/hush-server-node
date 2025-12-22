using Microsoft.EntityFrameworkCore;
using HushShared.Reactions.Model;

namespace HushNode.Reactions.Storage;

public class ReactionsDbContext(
    ReactionsDbContextConfigurator reactionsDbContextConfigurator,
    DbContextOptions<ReactionsDbContext> options) : DbContext(options)
{
    private readonly ReactionsDbContextConfigurator _reactionsDbContextConfigurator = reactionsDbContextConfigurator;

    public DbSet<MessageReactionTally> MessageReactionTallies { get; set; }
    public DbSet<ReactionNullifier> ReactionNullifiers { get; set; }
    public DbSet<ReactionTransaction> ReactionTransactions { get; set; }
    public DbSet<MerkleRootHistory> MerkleRootHistories { get; set; }
    public DbSet<FeedMemberCommitment> FeedMemberCommitments { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        this._reactionsDbContextConfigurator.Configure(modelBuilder);
    }
}
