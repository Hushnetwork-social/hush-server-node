using HushServerNode.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HushServerNode.InternalModule.Feed.Cache;

public class CacheFeedDbContextConfigurator : IDbContextConfigurator
{
    public void Configure(ModelBuilder modelBuilder)
    {
            modelBuilder
                .Entity<FeedEntity>()
                .ToTable("FeedEntity")
                .HasKey(x => x.FeedId);
            modelBuilder
                .Entity<FeedEntity>()
                .HasMany(x => x.FeedParticipants)
                .WithOne(x => x.Feed)
                .HasForeignKey(x => x.FeedId);

            modelBuilder
                .Entity<FeedParticipants>()
                .ToTable("FeedParticipants")
                .HasKey(x => new 
                {
                    x.FeedId,
                    x.ParticipantPublicAddress
                });

            modelBuilder
                .Entity<FeedMessageEntity>()
                .ToTable("FeedMessageEntity")
                .HasKey(x => x.FeedMessageId);
    }
}
