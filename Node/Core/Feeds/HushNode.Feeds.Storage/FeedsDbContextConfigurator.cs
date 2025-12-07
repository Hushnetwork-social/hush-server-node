using Microsoft.EntityFrameworkCore;
using HushNode.Interfaces;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using HushShared.Blockchain.Model;
using System.IO.Compression;

namespace HushNode.Feeds.Storage;

public class FeedsDbContextConfigurator : IDbContextConfigurator
{
    public void Configure(ModelBuilder modelBuilder)
    {
        ConfigureFeed(modelBuilder);
        ConfigureFeedParticipant(modelBuilder);
        ConfigureFeedMessage(modelBuilder);
    }

    private static void ConfigureFeedMessage(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<FeedMessage>(feedMessage => 
            {
                feedMessage.ToTable("FeedMessage", "Feeds");
                feedMessage.HasKey(x => x.FeedMessageId);

                feedMessage.Property(x => x.FeedMessageId)
                    .HasConversion(
                        x => x.ToString(),
                        x => FeedMessageIdHandler.CreateFromString(x)
                    )
                    .HasColumnType("varchar(40)");

                feedMessage.Property(x => x.FeedId)
                    .HasConversion(
                        x => x.ToString(),
                        x => FeedIdHandler.CreateFromString(x)
                    )
                    .HasColumnType("varchar(40)");

                feedMessage.Property(x => x.Timestamp)
                    .HasConversion(
                        x => x.ToString(),
                        x => TimestampHandler.CreateFromString(x)
                    )
                    .HasColumnType("varchar(40)");

                feedMessage.Property(x => x.BlockIndex)
                    .HasConversion(
                        x => x.Value,
                        x => new BlockIndex(x)
                    )
                    .HasColumnType("bigint");

                feedMessage.Property(x => x.IssuerPublicAddress)
                    .HasColumnType("varchar(200)");

                feedMessage.Property(x => x.MessageContent)
                    .HasColumnType("text");
            });
    }

    private static void ConfigureFeedParticipant(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<FeedParticipant>(feedParticipant => 
            {
                feedParticipant.ToTable("FeedParticipant", "Feeds");
                feedParticipant.HasKey(x => new { x.FeedId, x.ParticipantPublicAddress });

                feedParticipant
                    .Property(x => x.FeedId)
                    .HasConversion(
                        x => x.ToString(),
                        x => FeedIdHandler.CreateFromString(x)
                    )
                    .HasColumnType("varchar(40)");

                feedParticipant
                    .HasOne(x => x.Feed)
                    .WithMany(x => x.Participants)
                    .HasForeignKey(x => x.FeedId);

            });
    }

    private static void ConfigureFeed(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<Feed>(feed => 
            {
                feed.ToTable("Feed", "Feeds");
                feed.HasKey(x => x.FeedId);

                feed.Property(x => x.FeedId)
                    .HasConversion(
                        x => x.ToString(),
                        x => FeedIdHandler.CreateFromString(x)
                    )
                    .HasColumnType("varchar(40)");

                feed.Property(x => x.BlockIndex)
                    .HasConversion(
                        x => x.Value,
                        x => new BlockIndex(x)
                    )
                    .HasColumnType("bigint");

                feed.HasMany(x => x.Participants)
                    .WithOne()
                    .HasForeignKey(x => x.FeedId);
            });
    }
}
