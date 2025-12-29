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
        ConfigureGroupFeed(modelBuilder);
        ConfigureGroupFeedParticipant(modelBuilder);
        ConfigureGroupFeedKeyGeneration(modelBuilder);
        ConfigureGroupFeedEncryptedKey(modelBuilder);
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

                // Protocol Omega: Author commitment for ZK proof verification
                feedMessage.Property(x => x.AuthorCommitment)
                    .HasColumnType("bytea")
                    .HasMaxLength(32)
                    .IsRequired(false);

                // Reply to Message: Reference to parent message
                feedMessage.Property(x => x.ReplyToMessageId)
                    .HasConversion(
                        x => x != null ? x.ToString() : null,
                        x => x != null ? FeedMessageIdHandler.CreateFromString(x) : null
                    )
                    .HasColumnType("varchar(40)")
                    .IsRequired(false);

                feedMessage.HasIndex(x => x.ReplyToMessageId);
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
                    .Property(x => x.ParticipantPublicAddress)
                    .HasColumnType("varchar(500)");

                // EncryptedFeedKey stores the feed's AES-256 key encrypted with RSA
                // RSA-2048 encrypted output is ~344 characters in Base64
                feedParticipant
                    .Property(x => x.EncryptedFeedKey)
                    .HasColumnType("text");

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

    private static void ConfigureGroupFeed(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<GroupFeed>(groupFeed =>
            {
                groupFeed.ToTable("GroupFeed", "Feeds");
                groupFeed.HasKey(x => x.FeedId);

                groupFeed.Property(x => x.FeedId)
                    .HasConversion(
                        x => x.ToString(),
                        x => FeedIdHandler.CreateFromString(x)
                    )
                    .HasColumnType("varchar(40)");

                groupFeed.Property(x => x.Title)
                    .HasColumnType("text")
                    .IsRequired();

                groupFeed.Property(x => x.Description)
                    .HasColumnType("text")
                    .IsRequired();

                groupFeed.Property(x => x.IsPublic)
                    .HasColumnType("boolean")
                    .HasDefaultValue(false);

                groupFeed.Property(x => x.CreatedAtBlock)
                    .HasConversion(
                        x => x.Value,
                        x => new BlockIndex(x)
                    )
                    .HasColumnType("bigint");

                groupFeed.Property(x => x.CurrentKeyGeneration)
                    .HasColumnType("int")
                    .HasDefaultValue(0);

                groupFeed.HasMany(x => x.Participants)
                    .WithOne(x => x.GroupFeed)
                    .HasForeignKey(x => x.FeedId);

                groupFeed.HasMany(x => x.KeyGenerations)
                    .WithOne(x => x.GroupFeed)
                    .HasForeignKey(x => x.FeedId);
            });
    }

    private static void ConfigureGroupFeedParticipant(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<GroupFeedParticipantEntity>(participant =>
            {
                participant.ToTable("GroupFeedParticipant", "Feeds");
                participant.HasKey(x => new { x.FeedId, x.ParticipantPublicAddress });

                participant.Property(x => x.FeedId)
                    .HasConversion(
                        x => x.ToString(),
                        x => FeedIdHandler.CreateFromString(x)
                    )
                    .HasColumnType("varchar(40)");

                participant.Property(x => x.ParticipantPublicAddress)
                    .HasColumnType("varchar(500)");

                participant.Property(x => x.ParticipantType)
                    .HasConversion<int>()
                    .HasColumnType("int");

                participant.Property(x => x.JoinedAtBlock)
                    .HasConversion(
                        x => x.Value,
                        x => new BlockIndex(x)
                    )
                    .HasColumnType("bigint");

                participant.Property(x => x.LeftAtBlock)
                    .HasConversion(
                        x => x != null ? x.Value : (long?)null,
                        x => x != null ? new BlockIndex(x.Value) : null
                    )
                    .HasColumnType("bigint")
                    .IsRequired(false);

                participant.Property(x => x.LastLeaveBlock)
                    .HasConversion(
                        x => x != null ? x.Value : (long?)null,
                        x => x != null ? new BlockIndex(x.Value) : null
                    )
                    .HasColumnType("bigint")
                    .IsRequired(false);

                participant.HasOne(x => x.GroupFeed)
                    .WithMany(x => x.Participants)
                    .HasForeignKey(x => x.FeedId);
            });
    }

    private static void ConfigureGroupFeedKeyGeneration(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<GroupFeedKeyGenerationEntity>(keyGen =>
            {
                keyGen.ToTable("GroupFeedKeyGeneration", "Feeds");
                keyGen.HasKey(x => new { x.FeedId, x.KeyGeneration });

                keyGen.Property(x => x.FeedId)
                    .HasConversion(
                        x => x.ToString(),
                        x => FeedIdHandler.CreateFromString(x)
                    )
                    .HasColumnType("varchar(40)");

                keyGen.Property(x => x.KeyGeneration)
                    .HasColumnType("int");

                keyGen.Property(x => x.ValidFromBlock)
                    .HasConversion(
                        x => x.Value,
                        x => new BlockIndex(x)
                    )
                    .HasColumnType("bigint");

                keyGen.Property(x => x.RotationTrigger)
                    .HasConversion<int>()
                    .HasColumnType("int");

                keyGen.HasOne(x => x.GroupFeed)
                    .WithMany(x => x.KeyGenerations)
                    .HasForeignKey(x => x.FeedId);

                keyGen.HasMany(x => x.EncryptedKeys)
                    .WithOne(x => x.KeyGenerationEntity)
                    .HasForeignKey(x => new { x.FeedId, x.KeyGeneration });
            });
    }

    private static void ConfigureGroupFeedEncryptedKey(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<GroupFeedEncryptedKeyEntity>(encKey =>
            {
                encKey.ToTable("GroupFeedEncryptedKey", "Feeds");
                encKey.HasKey(x => new { x.FeedId, x.KeyGeneration, x.MemberPublicAddress });

                encKey.Property(x => x.FeedId)
                    .HasConversion(
                        x => x.ToString(),
                        x => FeedIdHandler.CreateFromString(x)
                    )
                    .HasColumnType("varchar(40)");

                encKey.Property(x => x.KeyGeneration)
                    .HasColumnType("int");

                encKey.Property(x => x.MemberPublicAddress)
                    .HasColumnType("varchar(500)");

                // ECIES encrypted AES key (~500 bytes Base64)
                encKey.Property(x => x.EncryptedAesKey)
                    .HasColumnType("text");

                encKey.HasOne(x => x.KeyGenerationEntity)
                    .WithMany(x => x.EncryptedKeys)
                    .HasForeignKey(x => new { x.FeedId, x.KeyGeneration });
            });
    }
}
