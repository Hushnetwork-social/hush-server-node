using Microsoft.EntityFrameworkCore;
using HushNode.Interfaces;
using HushShared.Reactions.Model;
using HushShared.Feeds.Model;
using HushShared.Blockchain.BlockModel;

namespace HushNode.Reactions.Storage;

public class ReactionsDbContextConfigurator : IDbContextConfigurator
{
    public void Configure(ModelBuilder modelBuilder)
    {
        ConfigureMessageReactionTally(modelBuilder);
        ConfigureReactionNullifier(modelBuilder);
        ConfigureReactionTransaction(modelBuilder);
        ConfigureMerkleRootHistory(modelBuilder);
        ConfigureFeedMemberCommitment(modelBuilder);
    }

    private static void ConfigureMessageReactionTally(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MessageReactionTally>(entity =>
        {
            entity.ToTable("MessageReactionTally", "Reactions");
            entity.HasKey(x => x.MessageId);

            entity.Property(x => x.MessageId)
                .HasConversion(
                    x => x.ToString(),
                    x => FeedMessageIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");

            entity.Property(x => x.FeedId)
                .HasConversion(
                    x => x.ToString(),
                    x => FeedIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");

            // BYTEA arrays for encrypted tally points
            entity.Property(x => x.TallyC1X).HasColumnType("bytea[]");
            entity.Property(x => x.TallyC1Y).HasColumnType("bytea[]");
            entity.Property(x => x.TallyC2X).HasColumnType("bytea[]");
            entity.Property(x => x.TallyC2Y).HasColumnType("bytea[]");

            // Version counter for incremental sync
            entity.Property(x => x.Version).HasDefaultValue(0L);

            entity.HasIndex(x => x.FeedId);
            entity.HasIndex(x => x.LastUpdated);

            // Index for efficient feed + version querying (reaction sync)
            entity.HasIndex(x => new { x.FeedId, x.Version })
                .HasDatabaseName("IX_MessageReactionTally_FeedId_Version");
        });
    }

    private static void ConfigureReactionNullifier(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReactionNullifier>(entity =>
        {
            entity.ToTable("ReactionNullifier", "Reactions");
            entity.HasKey(x => x.Nullifier);

            entity.Property(x => x.Nullifier)
                .HasColumnType("bytea")
                .HasMaxLength(32);

            entity.Property(x => x.MessageId)
                .HasConversion(
                    x => x.ToString(),
                    x => FeedMessageIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");

            entity.Property(x => x.VoteC1X).HasColumnType("bytea[]");
            entity.Property(x => x.VoteC1Y).HasColumnType("bytea[]");
            entity.Property(x => x.VoteC2X).HasColumnType("bytea[]");
            entity.Property(x => x.VoteC2Y).HasColumnType("bytea[]");

            entity.Property(x => x.EncryptedEmojiBackup)
                .HasColumnType("bytea")
                .IsRequired(false);

            entity.HasIndex(x => x.MessageId);
            entity.HasIndex(x => x.UpdatedAt);
        });
    }

    private static void ConfigureReactionTransaction(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReactionTransaction>(entity =>
        {
            entity.ToTable("ReactionTransaction", "Reactions");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.FeedId)
                .HasConversion(
                    x => x.ToString(),
                    x => FeedIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");

            entity.Property(x => x.MessageId)
                .HasConversion(
                    x => x.ToString(),
                    x => FeedMessageIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");

            entity.Property(x => x.BlockHeight)
                .HasConversion(
                    x => x.Value,
                    x => new BlockIndex(x))
                .HasColumnType("bigint");

            entity.Property(x => x.Nullifier).HasColumnType("bytea").HasMaxLength(32);
            entity.Property(x => x.ZkProof).HasColumnType("bytea");
            entity.Property(x => x.CircuitVersion).HasColumnType("varchar(20)");

            entity.Property(x => x.CiphertextC1X).HasColumnType("bytea[]");
            entity.Property(x => x.CiphertextC1Y).HasColumnType("bytea[]");
            entity.Property(x => x.CiphertextC2X).HasColumnType("bytea[]");
            entity.Property(x => x.CiphertextC2Y).HasColumnType("bytea[]");

            entity.HasIndex(x => x.BlockHeight);
            entity.HasIndex(x => x.MessageId);
            entity.HasIndex(x => x.Nullifier);
            entity.HasIndex(x => x.FeedId);
        });
    }

    private static void ConfigureMerkleRootHistory(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MerkleRootHistory>(entity =>
        {
            entity.ToTable("MerkleRootHistory", "Reactions");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).ValueGeneratedOnAdd();

            entity.Property(x => x.FeedId)
                .HasConversion(
                    x => x.ToString(),
                    x => FeedIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");

            entity.Property(x => x.MerkleRoot)
                .HasColumnType("bytea")
                .HasMaxLength(32);

            entity.HasIndex(x => new { x.FeedId, x.CreatedAt })
                .IsDescending(false, true);  // For grace period queries
            entity.HasIndex(x => x.BlockHeight);
        });
    }

    private static void ConfigureFeedMemberCommitment(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FeedMemberCommitment>(entity =>
        {
            entity.ToTable("FeedMemberCommitment", "Reactions");

            // Composite primary key (feed_id, user_commitment)
            entity.HasKey(x => new { x.FeedId, x.UserCommitment });

            entity.Property(x => x.FeedId)
                .HasConversion(
                    x => x.ToString(),
                    x => FeedIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");

            entity.Property(x => x.UserCommitment)
                .HasColumnType("bytea")
                .HasMaxLength(32);

            // IMPORTANT: NO navigation property to FeedParticipant!
            // This is intentional for privacy - we cannot link commitment to identity.

            entity.HasIndex(x => x.FeedId);
            entity.HasIndex(x => x.RegisteredAt);
        });
    }
}
