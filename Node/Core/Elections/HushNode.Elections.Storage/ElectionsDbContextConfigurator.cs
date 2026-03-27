using System.Text.Json;
using HushNode.Interfaces;
using HushShared.Elections.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HushNode.Elections.Storage;

public class ElectionsDbContextConfigurator : IDbContextConfigurator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(ModelBuilder modelBuilder)
    {
        ConfigureElectionRecord(modelBuilder);
        ConfigureElectionDraftSnapshot(modelBuilder);
        ConfigureElectionBoundaryArtifact(modelBuilder);
        ConfigureElectionWarningAcknowledgement(modelBuilder);
        ConfigureElectionTrusteeInvitation(modelBuilder);
        ConfigureElectionGovernedProposal(modelBuilder);
        ConfigureElectionGovernedProposalApproval(modelBuilder);
        ConfigureElectionCeremonyProfile(modelBuilder);
        ConfigureElectionCeremonyVersion(modelBuilder);
        ConfigureElectionCeremonyTranscriptEvent(modelBuilder);
        ConfigureElectionCeremonyMessageEnvelope(modelBuilder);
        ConfigureElectionCeremonyTrusteeState(modelBuilder);
        ConfigureElectionCeremonyShareCustody(modelBuilder);
    }

    private static void ConfigureElectionRecord(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionRecord>(entity =>
        {
            entity.ToTable("ElectionRecord", "Elections");
            entity.HasKey(x => x.ElectionId);

            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");

            entity.Property(x => x.Title).HasColumnType("text");
            entity.Property(x => x.ShortDescription).HasColumnType("text");
            entity.Property(x => x.OwnerPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.ExternalReferenceCode).HasColumnType("varchar(256)");
            entity.Property(x => x.LifecycleState).HasConversion<string>().HasColumnType("varchar(32)");
            entity.Property(x => x.ElectionClass).HasConversion<string>().HasColumnType("varchar(64)");
            entity.Property(x => x.BindingStatus).HasConversion<string>().HasColumnType("varchar(32)");
            entity.Property(x => x.GovernanceMode).HasConversion<string>().HasColumnType("varchar(32)");
            entity.Property(x => x.DisclosureMode).HasConversion<string>().HasColumnType("varchar(64)");
            entity.Property(x => x.ParticipationPrivacyMode).HasConversion<string>().HasColumnType("varchar(96)");
            entity.Property(x => x.VoteUpdatePolicy).HasConversion<string>().HasColumnType("varchar(64)");
            entity.Property(x => x.EligibilitySourceType).HasConversion<string>().HasColumnType("varchar(64)");
            entity.Property(x => x.EligibilityMutationPolicy).HasConversion<string>().HasColumnType("varchar(96)");
            entity.Property(x => x.ProtocolOmegaVersion).HasColumnType("varchar(64)");
            entity.Property(x => x.ReportingPolicy).HasConversion<string>().HasColumnType("varchar(64)");
            entity.Property(x => x.ReviewWindowPolicy).HasConversion<string>().HasColumnType("varchar(64)");
            entity.Property(x => x.CurrentDraftRevision).HasColumnType("integer");
            entity.Property(x => x.RequiredApprovalCount).HasColumnType("integer");
            entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.LastUpdatedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.OpenedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.VoteAcceptanceLockedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.ClosedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.FinalizedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.TallyReadyAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.OpenArtifactId).HasColumnType("uuid");
            entity.Property(x => x.CloseArtifactId).HasColumnType("uuid");
            entity.Property(x => x.FinalizeArtifactId).HasColumnType("uuid");

            ConfigureJsonProperty(entity.Property(x => x.OutcomeRule));
            ConfigureJsonProperty(entity.Property(x => x.ApprovedClientApplications));
            ConfigureJsonProperty(entity.Property(x => x.Options));
            ConfigureJsonProperty(entity.Property(x => x.AcknowledgedWarningCodes));

            entity.HasIndex(x => x.OwnerPublicAddress);
            entity.HasIndex(x => x.LifecycleState);
            entity.HasIndex(x => x.GovernanceMode);
            entity.HasIndex(x => x.LastUpdatedAt);
        });
    }

    private static void ConfigureElectionDraftSnapshot(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionDraftSnapshotRecord>(entity =>
        {
            entity.ToTable("ElectionDraftSnapshotRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.DraftRevision).HasColumnType("integer");
            entity.Property(x => x.SnapshotReason).HasColumnType("text");
            entity.Property(x => x.RecordedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.RecordedByPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.SourceTransactionId).HasColumnType("uuid");
            entity.Property(x => x.SourceBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.SourceBlockId).HasColumnType("uuid");

            ConfigureJsonProperty(entity.Property(x => x.Metadata));
            ConfigureJsonProperty(entity.Property(x => x.Policy));
            ConfigureJsonProperty(entity.Property(x => x.Options));
            ConfigureJsonProperty(entity.Property(x => x.AcknowledgedWarningCodes));

            entity.HasIndex(x => new { x.ElectionId, x.DraftRevision }).IsUnique();
        });
    }

    private static void ConfigureElectionBoundaryArtifact(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionBoundaryArtifactRecord>(entity =>
        {
            entity.ToTable("ElectionBoundaryArtifactRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.ArtifactType).HasConversion<string>().HasColumnType("varchar(24)");
            entity.Property(x => x.LifecycleState).HasConversion<string>().HasColumnType("varchar(24)");
            entity.Property(x => x.SourceDraftRevision).HasColumnType("integer");
            entity.Property(x => x.FrozenEligibleVoterSetHash).HasColumnType("bytea");
            entity.Property(x => x.TrusteePolicyExecutionReference).HasColumnType("text");
            entity.Property(x => x.ReportingPolicyExecutionReference).HasColumnType("text");
            entity.Property(x => x.ReviewWindowExecutionReference).HasColumnType("text");
            entity.Property(x => x.AcceptedBallotSetHash).HasColumnType("bytea");
            entity.Property(x => x.FinalEncryptedTallyHash).HasColumnType("bytea");
            entity.Property(x => x.RecordedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.RecordedByPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.SourceTransactionId).HasColumnType("uuid");
            entity.Property(x => x.SourceBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.SourceBlockId).HasColumnType("uuid");

            ConfigureJsonProperty(entity.Property(x => x.Metadata));
            ConfigureJsonProperty(entity.Property(x => x.Policy));
            ConfigureJsonProperty(entity.Property(x => x.Options));
            ConfigureJsonProperty(entity.Property(x => x.AcknowledgedWarningCodes));
            ConfigureJsonProperty(entity.Property(x => x.TrusteeSnapshot));

            entity.HasIndex(x => new { x.ElectionId, x.ArtifactType }).IsUnique();
        });
    }

    private static void ConfigureElectionWarningAcknowledgement(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionWarningAcknowledgementRecord>(entity =>
        {
            entity.ToTable("ElectionWarningAcknowledgementRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.WarningCode).HasConversion<string>().HasColumnType("varchar(64)");
            entity.Property(x => x.DraftRevision).HasColumnType("integer");
            entity.Property(x => x.AcknowledgedByPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.AcknowledgedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.SourceTransactionId).HasColumnType("uuid");
            entity.Property(x => x.SourceBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.SourceBlockId).HasColumnType("uuid");

            entity.HasIndex(x => new { x.ElectionId, x.WarningCode, x.DraftRevision }).IsUnique();
        });
    }

    private static void ConfigureElectionTrusteeInvitation(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionTrusteeInvitationRecord>(entity =>
        {
            entity.ToTable("ElectionTrusteeInvitationRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.TrusteeUserAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.TrusteeDisplayName).HasColumnType("varchar(200)");
            entity.Property(x => x.InvitedByPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.LinkedMessageId).HasColumnType("uuid");
            entity.Property(x => x.Status).HasConversion<string>().HasColumnType("varchar(24)");
            entity.Property(x => x.SentAtDraftRevision).HasColumnType("integer");
            entity.Property(x => x.SentAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.ResolvedAtDraftRevision).HasColumnType("integer");
            entity.Property(x => x.RespondedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.RevokedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.LatestTransactionId).HasColumnType("uuid");
            entity.Property(x => x.LatestBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.LatestBlockId).HasColumnType("uuid");

            entity.HasIndex(x => x.ElectionId);
            entity.HasIndex(x => new { x.ElectionId, x.Status });
            entity.HasIndex(x => new { x.ElectionId, x.TrusteeUserAddress });
        });
    }

    private static void ConfigureElectionGovernedProposal(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionGovernedProposalRecord>(entity =>
        {
            entity.ToTable("ElectionGovernedProposalRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.ActionType).HasConversion<string>().HasColumnType("varchar(24)");
            entity.Property(x => x.LifecycleStateAtCreation).HasConversion<string>().HasColumnType("varchar(24)");
            entity.Property(x => x.ProposedByPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.ExecutionStatus).HasConversion<string>().HasColumnType("varchar(32)");
            entity.Property(x => x.LastExecutionAttemptedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.ExecutedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.ExecutionFailureReason).HasColumnType("text");
            entity.Property(x => x.LastExecutionTriggeredByPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.LatestTransactionId).HasColumnType("uuid");
            entity.Property(x => x.LatestBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.LatestBlockId).HasColumnType("uuid");

            entity.HasIndex(x => x.ElectionId);
            entity.HasIndex(x => new { x.ElectionId, x.ExecutionStatus });
            entity.HasIndex(x => new { x.ElectionId, x.CreatedAt });
        });
    }

    private static void ConfigureElectionGovernedProposalApproval(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionGovernedProposalApprovalRecord>(entity =>
        {
            entity.ToTable("ElectionGovernedProposalApprovalRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ProposalId).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.ActionType).HasConversion<string>().HasColumnType("varchar(24)");
            entity.Property(x => x.LifecycleStateAtProposalCreation).HasConversion<string>().HasColumnType("varchar(24)");
            entity.Property(x => x.TrusteeUserAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.TrusteeDisplayName).HasColumnType("varchar(200)");
            entity.Property(x => x.ApprovalNote).HasColumnType("text");
            entity.Property(x => x.ApprovedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.SourceTransactionId).HasColumnType("uuid");
            entity.Property(x => x.SourceBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.SourceBlockId).HasColumnType("uuid");

            entity.HasIndex(x => x.ProposalId);
            entity.HasIndex(x => new { x.ProposalId, x.TrusteeUserAddress }).IsUnique();
            entity.HasIndex(x => new { x.ElectionId, x.ActionType });
        });
    }

    private static void ConfigureElectionCeremonyProfile(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionCeremonyProfileRecord>(entity =>
        {
            entity.ToTable("ElectionCeremonyProfileRecord", "Elections");
            entity.HasKey(x => x.ProfileId);

            entity.Property(x => x.ProfileId).HasColumnType("varchar(96)");
            entity.Property(x => x.DisplayName).HasColumnType("varchar(200)");
            entity.Property(x => x.Description).HasColumnType("text");
            entity.Property(x => x.ProviderKey).HasColumnType("varchar(96)");
            entity.Property(x => x.ProfileVersion).HasColumnType("varchar(64)");
            entity.Property(x => x.TrusteeCount).HasColumnType("integer");
            entity.Property(x => x.RequiredApprovalCount).HasColumnType("integer");
            entity.Property(x => x.DevOnly).HasColumnType("boolean");
            entity.Property(x => x.RegisteredAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.LastUpdatedAt).HasColumnType("timestamp with time zone");

            entity.HasIndex(x => x.DevOnly);
            entity.HasIndex(x => new { x.TrusteeCount, x.RequiredApprovalCount });
        });
    }

    private static void ConfigureElectionCeremonyVersion(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionCeremonyVersionRecord>(entity =>
        {
            entity.ToTable("ElectionCeremonyVersionRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.VersionNumber).HasColumnType("integer");
            entity.Property(x => x.ProfileId).HasColumnType("varchar(96)");
            entity.Property(x => x.Status).HasConversion<string>().HasColumnType("varchar(24)");
            entity.Property(x => x.TrusteeCount).HasColumnType("integer");
            entity.Property(x => x.RequiredApprovalCount).HasColumnType("integer");
            entity.Property(x => x.StartedByPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.StartedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.CompletedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.SupersededAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.SupersededReason).HasColumnType("text");
            entity.Property(x => x.TallyPublicKeyFingerprint).HasColumnType("varchar(256)");

            ConfigureJsonProperty(entity.Property(x => x.BoundTrustees));

            entity.HasIndex(x => new { x.ElectionId, x.VersionNumber }).IsUnique();
            entity.HasIndex(x => new { x.ElectionId, x.Status });
        });
    }

    private static void ConfigureElectionCeremonyTranscriptEvent(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionCeremonyTranscriptEventRecord>(entity =>
        {
            entity.ToTable("ElectionCeremonyTranscriptEventRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.CeremonyVersionId).HasColumnType("uuid");
            entity.Property(x => x.VersionNumber).HasColumnType("integer");
            entity.Property(x => x.EventType).HasConversion<string>().HasColumnType("varchar(48)");
            entity.Property(x => x.ActorPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.TrusteeUserAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.TrusteeDisplayName).HasColumnType("varchar(200)");
            entity.Property(x => x.TrusteeState).HasConversion<string>().HasColumnType("varchar(40)");
            entity.Property(x => x.EventSummary).HasColumnType("text");
            entity.Property(x => x.EvidenceReference).HasColumnType("text");
            entity.Property(x => x.RestartReason).HasColumnType("text");
            entity.Property(x => x.TallyPublicKeyFingerprint).HasColumnType("varchar(256)");
            entity.Property(x => x.OccurredAt).HasColumnType("timestamp with time zone");

            entity.HasIndex(x => new { x.CeremonyVersionId, x.OccurredAt });
            entity.HasIndex(x => new { x.ElectionId, x.VersionNumber });
        });
    }

    private static void ConfigureElectionCeremonyMessageEnvelope(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionCeremonyMessageEnvelopeRecord>(entity =>
        {
            entity.ToTable("ElectionCeremonyMessageEnvelopeRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.CeremonyVersionId).HasColumnType("uuid");
            entity.Property(x => x.VersionNumber).HasColumnType("integer");
            entity.Property(x => x.ProfileId).HasColumnType("varchar(96)");
            entity.Property(x => x.SenderTrusteeUserAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.RecipientTrusteeUserAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.MessageType).HasColumnType("varchar(96)");
            entity.Property(x => x.PayloadVersion).HasColumnType("varchar(64)");
            entity.Property(x => x.EncryptedPayload).HasColumnType("bytea");
            entity.Property(x => x.PayloadFingerprint).HasColumnType("varchar(256)");
            entity.Property(x => x.SubmittedAt).HasColumnType("timestamp with time zone");

            entity.HasIndex(x => new { x.CeremonyVersionId, x.SubmittedAt });
            entity.HasIndex(x => new { x.CeremonyVersionId, x.RecipientTrusteeUserAddress });
        });
    }

    private static void ConfigureElectionCeremonyTrusteeState(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionCeremonyTrusteeStateRecord>(entity =>
        {
            entity.ToTable("ElectionCeremonyTrusteeStateRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.CeremonyVersionId).HasColumnType("uuid");
            entity.Property(x => x.TrusteeUserAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.TrusteeDisplayName).HasColumnType("varchar(200)");
            entity.Property(x => x.State).HasConversion<string>().HasColumnType("varchar(40)");
            entity.Property(x => x.TransportPublicKeyFingerprint).HasColumnType("varchar(256)");
            entity.Property(x => x.TransportPublicKeyPublishedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.JoinedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.SelfTestSucceededAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.MaterialSubmittedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.ValidationFailedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.ValidationFailureReason).HasColumnType("text");
            entity.Property(x => x.CompletedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.RemovedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.ShareVersion).HasColumnType("varchar(64)");
            entity.Property(x => x.LastUpdatedAt).HasColumnType("timestamp with time zone");

            entity.HasIndex(x => new { x.CeremonyVersionId, x.TrusteeUserAddress }).IsUnique();
            entity.HasIndex(x => new { x.ElectionId, x.State });
        });
    }

    private static void ConfigureElectionCeremonyShareCustody(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionCeremonyShareCustodyRecord>(entity =>
        {
            entity.ToTable("ElectionCeremonyShareCustodyRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.CeremonyVersionId).HasColumnType("uuid");
            entity.Property(x => x.TrusteeUserAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.ShareVersion).HasColumnType("varchar(64)");
            entity.Property(x => x.PasswordProtected).HasColumnType("boolean");
            entity.Property(x => x.Status).HasConversion<string>().HasColumnType("varchar(32)");
            entity.Property(x => x.LastExportedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.LastImportedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.LastImportFailedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.LastImportFailureReason).HasColumnType("text");
            entity.Property(x => x.LastUpdatedAt).HasColumnType("timestamp with time zone");

            entity.HasIndex(x => new { x.CeremonyVersionId, x.TrusteeUserAddress }).IsUnique();
            entity.HasIndex(x => new { x.ElectionId, x.Status });
        });
    }

    private static void ConfigureJsonProperty<T>(PropertyBuilder<T> propertyBuilder)
    {
        var converter = new ValueConverter<T, string>(
            value => JsonSerializer.Serialize(value, JsonOptions),
            value => JsonSerializer.Deserialize<T>(value, JsonOptions)!);

        var comparer = new ValueComparer<T>(
            (left, right) => JsonSerializer.Serialize(left, JsonOptions) == JsonSerializer.Serialize(right, JsonOptions),
            value => JsonSerializer.Serialize(value, JsonOptions).GetHashCode(StringComparison.Ordinal),
            value => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)!);

        propertyBuilder.HasConversion(converter);
        propertyBuilder.Metadata.SetValueComparer(comparer);
        propertyBuilder.HasColumnType("jsonb");
    }
}
