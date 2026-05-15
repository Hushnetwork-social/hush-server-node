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
        ConfigureElectionEnvelopeAccess(modelBuilder);
        ConfigureElectionResultArtifact(modelBuilder);
        ConfigureElectionReportPackage(modelBuilder);
        ConfigureElectionReportArtifact(modelBuilder);
        ConfigureElectionReportAccessGrant(modelBuilder);
        ConfigureApprovedProtocolPackageCatalogEntry(modelBuilder);
        ConfigureProtocolPackageBinding(modelBuilder);
        ConfigureElectionRosterEntry(modelBuilder);
        ConfigureElectionEligibilityActivationEvent(modelBuilder);
        ConfigureElectionParticipation(modelBuilder);
        ConfigureElectionCommitmentRegistration(modelBuilder);
        ConfigureElectionVoterCeremony(modelBuilder);
        ConfigureElectionPreparedBallotCommitment(modelBuilder);
        ConfigureElectionSpoiledPreparedBallot(modelBuilder);
        ConfigureElectionCheckoffConsumption(modelBuilder);
        ConfigureElectionEligibilitySnapshot(modelBuilder);
        ConfigureElectionRosterImportEvidence(modelBuilder);
        ConfigureElectionEligibilityPolicyEvidence(modelBuilder);
        ConfigureElectionCommitmentSchemeEvidence(modelBuilder);
        ConfigureElectionBoundaryArtifact(modelBuilder);
        ConfigureElectionAcceptedBallot(modelBuilder);
        ConfigureElectionBallotMemPool(modelBuilder);
        ConfigureElectionPublishedBallot(modelBuilder);
        ConfigureElectionCastIdempotency(modelBuilder);
        ConfigureElectionPublicationIssue(modelBuilder);
        ConfigureElectionPublicationWitness(modelBuilder);
        ConfigureElectionPublicationProofSession(modelBuilder);
        ConfigureElectionPublicationProofTranscript(modelBuilder);
        ConfigureElectionPublicationWitnessDeletionReceipt(modelBuilder);
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
        ConfigureElectionFinalizationSession(modelBuilder);
        ConfigureElectionCloseCountingJob(modelBuilder);
        ConfigureElectionExecutorSessionKeyEnvelope(modelBuilder);
        ConfigureAdminOnlyProtectedTallyEnvelope(modelBuilder);
        ConfigureElectionTallyExecutorLease(modelBuilder);
        ConfigureElectionFinalizationShare(modelBuilder);
        ConfigureElectionFinalizationReleaseEvidence(modelBuilder);
        ConfigureElectionAnomalyRecords(modelBuilder);
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
            entity.Property(x => x.SelectedProfileId).HasColumnType("text");
            entity.Property(x => x.SelectedProfileDevOnly).HasColumnType("boolean");
            entity.Property(x => x.GovernanceMode).HasConversion<string>().HasColumnType("varchar(32)");
            entity.Property(x => x.DisclosureMode).HasConversion<string>().HasColumnType("varchar(64)");
            entity.Property(x => x.ParticipationPrivacyMode).HasConversion<string>().HasColumnType("varchar(96)");
            entity.Property(x => x.VoteUpdatePolicy).HasConversion<string>().HasColumnType("varchar(64)");
            entity.Property(x => x.EligibilitySourceType).HasConversion<string>().HasColumnType("varchar(64)");
            entity.Property(x => x.EligibilityMutationPolicy).HasConversion<string>().HasColumnType("varchar(96)");
            entity.Property(x => x.IdentityLinkPolicy)
                .HasConversion<string>()
                .HasColumnType("varchar(96)")
                .HasDefaultValue(ElectionIdentityLinkPolicy.ContactCodeV1);
            entity.Property(x => x.CheckoffVisibilityPolicy)
                .HasConversion<string>()
                .HasColumnType("varchar(96)")
                .HasDefaultValue(ElectionCheckoffVisibilityPolicy.RestrictedOwnerAuditor);
            entity.Property(x => x.ActorLinkMultiplicityPolicy)
                .HasConversion<string>()
                .HasColumnType("varchar(96)")
                .HasDefaultValue(ElectionActorLinkMultiplicityPolicy.SingleRosterEntryPerActor);
            entity.Property(x => x.ContactCodeProviderReadiness)
                .HasConversion<string>()
                .HasColumnType("varchar(40)")
                .HasSentinel((ElectionContactCodeProviderReadiness)(-1))
                .HasDefaultValue(ElectionContactCodeProviderReadiness.DevOnly);
            entity.Property(x => x.ControlDomainProfileId).HasColumnType("varchar(96)");
            entity.Property(x => x.ControlDomainProfileVersion).HasColumnType("varchar(32)");
            entity.Property(x => x.ThresholdProfileId).HasColumnType("varchar(96)");
            entity.Property(x => x.ProtocolOmegaVersion).HasColumnType("varchar(64)");
            entity.Property(x => x.ReportingPolicy).HasConversion<string>().HasColumnType("varchar(64)");
            entity.Property(x => x.ReviewWindowPolicy).HasConversion<string>().HasColumnType("varchar(64)");
            entity.Property(x => x.OfficialResultVisibilityPolicy).HasConversion<string>().HasColumnType("varchar(40)");
            entity.Property(x => x.CurrentDraftRevision).HasColumnType("integer");
            entity.Property(x => x.RequiredApprovalCount).HasColumnType("integer");
            entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.LastUpdatedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.OpenedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.VoteAcceptanceLockedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.ClosedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.FinalizedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.TallyReadyAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.AnomalySubmissionWindowClosesAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.ClosedProgressStatus).HasConversion<string>().HasColumnType("varchar(40)");
            entity.Property(x => x.OpenArtifactId).HasColumnType("uuid");
            entity.Property(x => x.CloseArtifactId).HasColumnType("uuid");
            entity.Property(x => x.TallyReadyArtifactId).HasColumnType("uuid");
            entity.Property(x => x.FinalizeArtifactId).HasColumnType("uuid");
            entity.Property(x => x.UnofficialResultArtifactId).HasColumnType("uuid");
            entity.Property(x => x.OfficialResultArtifactId).HasColumnType("uuid");
            entity.Property(x => x.BallotDefinitionVersion).HasColumnType("integer");
            entity.Property(x => x.BallotDefinitionHash).HasColumnType("bytea");
            entity.Property(x => x.BallotDefinitionSealedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.BallotDefinitionMutationPolicy).HasConversion<string>().HasColumnType("varchar(40)");

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

    private static void ConfigureElectionEnvelopeAccess(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionEnvelopeAccessRecord>(entity =>
        {
            entity.ToTable("ElectionEnvelopeAccessRecord", "Elections");
            entity.HasKey(x => new { x.ElectionId, x.ActorPublicAddress });

            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.ActorPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.NodeEncryptedElectionPrivateKey).HasColumnType("text");
            entity.Property(x => x.ActorEncryptedElectionPrivateKey).HasColumnType("text");
            entity.Property(x => x.GrantedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.SourceTransactionId).HasColumnType("uuid");
            entity.Property(x => x.SourceBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.SourceBlockId).HasColumnType("uuid");

            entity.HasIndex(x => x.ActorPublicAddress);
        });
    }

    private static void ConfigureElectionResultArtifact(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionResultArtifactRecord>(entity =>
        {
            entity.ToTable("ElectionResultArtifactRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.ArtifactKind).HasConversion<string>().HasColumnType("varchar(24)");
            entity.Property(x => x.Visibility).HasConversion<string>().HasColumnType("varchar(32)");
            entity.Property(x => x.Title).HasColumnType("text");
            entity.Property(x => x.BlankCount).HasColumnType("integer");
            entity.Property(x => x.TotalVotedCount).HasColumnType("integer");
            entity.Property(x => x.EligibleToVoteCount).HasColumnType("integer");
            entity.Property(x => x.DidNotVoteCount).HasColumnType("integer");
            entity.Property(x => x.TallyReadyArtifactId).HasColumnType("uuid");
            entity.Property(x => x.SourceResultArtifactId).HasColumnType("uuid");
            entity.Property(x => x.EncryptedPayload).HasColumnType("text");
            entity.Property(x => x.PublicPayload).HasColumnType("text");
            entity.Property(x => x.RecordedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.RecordedByPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.SourceTransactionId).HasColumnType("uuid");
            entity.Property(x => x.SourceBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.SourceBlockId).HasColumnType("uuid");

            ConfigureJsonProperty(entity.Property(x => x.NamedOptionResults));
            ConfigureJsonProperty(entity.Property(x => x.DenominatorEvidence));

            entity.HasIndex(x => new { x.ElectionId, x.ArtifactKind }).IsUnique();
            entity.HasIndex(x => new { x.ElectionId, x.RecordedAt });
        });
    }

    private static void ConfigureElectionReportPackage(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionReportPackageRecord>(entity =>
        {
            entity.ToTable("ElectionReportPackageRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.AttemptNumber).HasColumnType("integer");
            entity.Property(x => x.PreviousAttemptId).HasColumnType("uuid");
            entity.Property(x => x.FinalizationSessionId).HasColumnType("uuid");
            entity.Property(x => x.TallyReadyArtifactId).HasColumnType("uuid");
            entity.Property(x => x.UnofficialResultArtifactId).HasColumnType("uuid");
            entity.Property(x => x.OfficialResultArtifactId).HasColumnType("uuid");
            entity.Property(x => x.FinalizeArtifactId).HasColumnType("uuid");
            entity.Property(x => x.CloseBoundaryArtifactId).HasColumnType("uuid");
            entity.Property(x => x.CloseEligibilitySnapshotId).HasColumnType("uuid");
            entity.Property(x => x.FinalizationReleaseEvidenceId).HasColumnType("uuid");
            entity.Property(x => x.Status).HasConversion<string>().HasColumnType("varchar(32)");
            entity.Property(x => x.FrozenEvidenceHash).HasColumnType("bytea");
            entity.Property(x => x.FrozenEvidenceFingerprint).HasColumnType("varchar(256)");
            entity.Property(x => x.PackageHash).HasColumnType("bytea");
            entity.Property(x => x.ArtifactCount).HasColumnType("integer");
            entity.Property(x => x.FailureCode).HasColumnType("varchar(128)");
            entity.Property(x => x.FailureReason).HasColumnType("text");
            entity.Property(x => x.AttemptedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.SealedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.AttemptedByPublicAddress).HasColumnType("varchar(160)");

            entity.HasIndex(x => new { x.ElectionId, x.AttemptNumber }).IsUnique();
            entity.HasIndex(x => new { x.ElectionId, x.AttemptedAt });
            entity.HasIndex(x => new { x.ElectionId, x.Status });
        });
    }

    private static void ConfigureElectionReportArtifact(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionReportArtifactRecord>(entity =>
        {
            entity.ToTable("ElectionReportArtifactRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ReportPackageId).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.ArtifactKind).HasConversion<string>().HasColumnType("varchar(64)");
            entity.Property(x => x.Format).HasConversion<string>().HasColumnType("varchar(16)");
            entity.Property(x => x.AccessScope).HasConversion<string>().HasColumnType("varchar(40)");
            entity.Property(x => x.SortOrder).HasColumnType("integer");
            entity.Property(x => x.Title).HasColumnType("text");
            entity.Property(x => x.FileName).HasColumnType("varchar(256)");
            entity.Property(x => x.MediaType).HasColumnType("varchar(128)");
            entity.Property(x => x.ContentHash).HasColumnType("bytea");
            entity.Property(x => x.Content).HasColumnType("text");
            entity.Property(x => x.PairedArtifactId).HasColumnType("uuid");
            entity.Property(x => x.RecordedAt).HasColumnType("timestamp with time zone");

            entity.HasIndex(x => new { x.ReportPackageId, x.ArtifactKind }).IsUnique();
            entity.HasIndex(x => new { x.ReportPackageId, x.SortOrder });
            entity.HasIndex(x => x.ElectionId);
        });
    }

    private static void ConfigureElectionReportAccessGrant(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionReportAccessGrantRecord>(entity =>
        {
            entity.ToTable("ElectionReportAccessGrantRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.ActorPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.GrantRole).HasConversion<string>().HasColumnType("varchar(32)");
            entity.Property(x => x.GrantedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.GrantedByPublicAddress).HasColumnType("varchar(160)");

            entity.HasIndex(x => new { x.ElectionId, x.ActorPublicAddress, x.GrantRole }).IsUnique();
            entity.HasIndex(x => x.ActorPublicAddress);
        });
    }

    private static void ConfigureApprovedProtocolPackageCatalogEntry(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApprovedProtocolPackageCatalogEntryRecord>(entity =>
        {
            entity.ToTable("ApprovedProtocolPackageCatalogEntryRecord", "Elections");
            entity.HasKey(x => new { x.PackageId, x.PackageVersion });

            entity.Property(x => x.PackageId).HasColumnType("varchar(160)");
            entity.Property(x => x.PackageVersion).HasColumnType("varchar(64)");
            entity.Property(x => x.SpecPackageHash).HasColumnType("varchar(64)");
            entity.Property(x => x.ProofPackageHash).HasColumnType("varchar(64)");
            entity.Property(x => x.ReleaseManifestHash).HasColumnType("varchar(64)");
            entity.Property(x => x.ApprovalStatus).HasConversion<string>().HasColumnType("varchar(32)");
            entity.Property(x => x.ApprovedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.IsLatestForCompatibleProfiles).HasColumnType("boolean");
            entity.Property(x => x.ExternalReviewStatus).HasConversion<string>().HasColumnType("varchar(40)");

            ConfigureJsonProperty(entity.Property(x => x.CompatibleProfileIds));
            ConfigureJsonProperty(entity.Property(x => x.SpecAccessLocations));
            ConfigureJsonProperty(entity.Property(x => x.ProofAccessLocations));

            entity.HasIndex(x => x.PackageId);
            entity.HasIndex(x => x.PackageVersion);
            entity.HasIndex(x => x.ApprovalStatus);
            entity.HasIndex(x => x.IsLatestForCompatibleProfiles);
        });
    }

    private static void ConfigureProtocolPackageBinding(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProtocolPackageBindingRecord>(entity =>
        {
            entity.ToTable("ProtocolPackageBindingRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.PackageId).HasColumnType("varchar(160)");
            entity.Property(x => x.PackageVersion).HasColumnType("varchar(64)");
            entity.Property(x => x.SelectedProfileId).HasColumnType("varchar(96)");
            entity.Property(x => x.SpecPackageHash).HasColumnType("varchar(64)");
            entity.Property(x => x.ProofPackageHash).HasColumnType("varchar(64)");
            entity.Property(x => x.ReleaseManifestHash).HasColumnType("varchar(64)");
            entity.Property(x => x.PackageApprovalStatus).HasConversion<string>().HasColumnType("varchar(32)");
            entity.Property(x => x.Status).HasConversion<string>().HasColumnType("varchar(32)");
            entity.Property(x => x.Source).HasConversion<string>().HasColumnType("varchar(32)");
            entity.Property(x => x.DraftRevision).HasColumnType("integer");
            entity.Property(x => x.BoundAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.SealedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.BoundByPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.ExternalReviewStatus).HasConversion<string>().HasColumnType("varchar(40)");
            entity.Property(x => x.SourceTransactionId).HasColumnType("uuid");
            entity.Property(x => x.SourceBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.SourceBlockId).HasColumnType("uuid");

            ConfigureJsonProperty(entity.Property(x => x.SpecAccessLocations));
            ConfigureJsonProperty(entity.Property(x => x.ProofAccessLocations));

            entity.HasIndex(x => new { x.ElectionId, x.BoundAt });
            entity.HasIndex(x => new { x.ElectionId, x.Status });
            entity.HasIndex(x => new { x.ElectionId, x.DraftRevision });
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
            entity.Property(x => x.AcceptedBallotCount).HasColumnType("integer");
            entity.Property(x => x.AcceptedBallotSetHash).HasColumnType("bytea");
            entity.Property(x => x.PublishedBallotCount).HasColumnType("integer");
            entity.Property(x => x.PublishedBallotStreamHash).HasColumnType("bytea");
            entity.Property(x => x.FinalEncryptedTallyHash).HasColumnType("bytea");
            entity.Property(x => x.RecordedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.RecordedByPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.SourceTransactionId).HasColumnType("uuid");
            entity.Property(x => x.SourceBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.SourceBlockId).HasColumnType("uuid");
            entity.Property(x => x.BallotDefinitionVersion).HasColumnType("integer");
            entity.Property(x => x.BallotDefinitionHash).HasColumnType("bytea");
            entity.Property(x => x.BallotDefinitionSealedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.BallotDefinitionMutationPolicy).HasConversion<string>().HasColumnType("varchar(40)");

            ConfigureJsonProperty(entity.Property(x => x.Metadata));
            ConfigureJsonProperty(entity.Property(x => x.Policy));
            ConfigureJsonProperty(entity.Property(x => x.Options));
            ConfigureJsonProperty(entity.Property(x => x.AcknowledgedWarningCodes));
            ConfigureJsonProperty(entity.Property(x => x.TrusteeSnapshot));
            ConfigureJsonProperty(entity.Property(x => x.CeremonySnapshot));

            entity.HasIndex(x => new { x.ElectionId, x.ArtifactType }).IsUnique();
        });
    }

    private static void ConfigureElectionVoterCeremony(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionVoterCeremonyRecord>(entity =>
        {
            entity.ToTable("ElectionVoterCeremonyRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.OrganizationVoterId).HasColumnType("varchar(128)");
            entity.Property(x => x.LinkedActorPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.CeremonyProfileId).HasColumnType("varchar(96)");
            entity.Property(x => x.BallotDefinitionVersion).HasColumnType("integer");
            entity.Property(x => x.BallotDefinitionHash).HasColumnType("bytea");
            entity.Property(x => x.PreparedPackageCount).HasColumnType("integer");
            entity.Property(x => x.SpoiledPackageCount).HasColumnType("integer");
            entity.Property(x => x.FinalState).HasConversion<string>().HasColumnType("varchar(32)");
            entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.LastUpdatedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.FinalAcceptedBallotId).HasColumnType("uuid");
            entity.Property(x => x.SourceTransactionId).HasColumnType("uuid");
            entity.Property(x => x.SourceBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.SourceBlockId).HasColumnType("uuid");

            entity.HasIndex(x => new { x.ElectionId, x.OrganizationVoterId }).IsUnique();
            entity.HasIndex(x => new { x.ElectionId, x.LinkedActorPublicAddress });
            entity.HasIndex(x => new { x.ElectionId, x.FinalState });
        });
    }

    private static void ConfigureElectionPreparedBallotCommitment(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionPreparedBallotCommitmentRecord>(entity =>
        {
            entity.ToTable("ElectionPreparedBallotCommitmentRecord", "Elections");
            entity.HasKey(x => x.PreparedBallotId);

            entity.Property(x => x.PreparedBallotId).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.OrganizationVoterId).HasColumnType("varchar(128)");
            entity.Property(x => x.LinkedActorPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.PreparedBallotHash).HasColumnType("varchar(256)");
            entity.Property(x => x.BallotDefinitionVersion).HasColumnType("integer");
            entity.Property(x => x.BallotDefinitionHash).HasColumnType("bytea");
            entity.Property(x => x.CeremonyProfileId).HasColumnType("varchar(96)");
            entity.Property(x => x.ProofStatementId).HasColumnType("varchar(160)");
            entity.Property(x => x.State).HasConversion<string>().HasColumnType("varchar(24)");
            entity.Property(x => x.PrecommittedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.ExpiresAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.SpoilMarkerId).HasColumnType("uuid");
            entity.Property(x => x.AcceptedBallotId).HasColumnType("uuid");
            entity.Property(x => x.SpoiledAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.CastAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.SourceTransactionId).HasColumnType("uuid");
            entity.Property(x => x.SourceBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.SourceBlockId).HasColumnType("uuid");

            entity.HasIndex(x => new { x.ElectionId, x.PreparedBallotHash }).IsUnique();
            entity.HasIndex(x => new { x.ElectionId, x.OrganizationVoterId, x.PrecommittedAt });
            entity.HasIndex(x => new { x.ElectionId, x.State });
        });
    }

    private static void ConfigureElectionSpoiledPreparedBallot(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionSpoiledPreparedBallotRecord>(entity =>
        {
            entity.ToTable("ElectionSpoiledPreparedBallotRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.PreparedBallotId).HasColumnType("uuid");
            entity.Property(x => x.PreparedBallotHash).HasColumnType("varchar(256)");
            entity.Property(x => x.SpoiledTranscriptHash).HasColumnType("varchar(256)");
            entity.Property(x => x.SpoilRecordHash).HasColumnType("varchar(256)");
            entity.Property(x => x.LocalVerifierVersion).HasColumnType("varchar(96)");
            entity.Property(x => x.SpoiledAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.SourceTransactionId).HasColumnType("uuid");
            entity.Property(x => x.SourceBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.SourceBlockId).HasColumnType("uuid");

            entity.HasIndex(x => new { x.ElectionId, x.PreparedBallotId }).IsUnique();
            entity.HasIndex(x => new { x.ElectionId, x.SpoilRecordHash }).IsUnique();
            entity.HasIndex(x => new { x.ElectionId, x.SpoiledAt });
        });
    }

    private static void ConfigureElectionRosterEntry(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionRosterEntryRecord>(entity =>
        {
            entity.ToTable("ElectionRosterEntryRecord", "Elections");
            entity.HasKey(x => new { x.ElectionId, x.OrganizationVoterId });

            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.OrganizationVoterId).HasColumnType("varchar(128)");
            entity.Property(x => x.ContactType).HasConversion<string>().HasColumnType("varchar(16)");
            entity.Property(x => x.ContactValue).HasColumnType("varchar(320)");
            entity.Property(x => x.LinkStatus).HasConversion<string>().HasColumnType("varchar(16)");
            entity.Property(x => x.LinkedActorPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.LinkedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.VotingRightStatus).HasConversion<string>().HasColumnType("varchar(16)");
            entity.Property(x => x.ImportedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.WasPresentAtOpen).HasColumnType("boolean");
            entity.Property(x => x.WasActiveAtOpen).HasColumnType("boolean");
            entity.Property(x => x.LastActivatedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.LastActivatedByPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.LastUpdatedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.LatestTransactionId).HasColumnType("uuid");
            entity.Property(x => x.LatestBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.LatestBlockId).HasColumnType("uuid");

            entity.HasIndex(x => new { x.ElectionId, x.LinkStatus });
            entity.HasIndex(x => new { x.ElectionId, x.VotingRightStatus });
            entity.HasIndex(x => new { x.ElectionId, x.LinkedActorPublicAddress });
            entity.HasIndex(x => new { x.ElectionId, x.WasPresentAtOpen, x.VotingRightStatus });
        });
    }

    private static void ConfigureElectionEligibilityActivationEvent(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionEligibilityActivationEventRecord>(entity =>
        {
            entity.ToTable("ElectionEligibilityActivationEventRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.OrganizationVoterId).HasColumnType("varchar(128)");
            entity.Property(x => x.AttemptedByPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.Outcome).HasConversion<string>().HasColumnType("varchar(16)");
            entity.Property(x => x.BlockReason).HasConversion<string>().HasColumnType("varchar(40)");
            entity.Property(x => x.OccurredAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.SourceTransactionId).HasColumnType("uuid");
            entity.Property(x => x.SourceBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.SourceBlockId).HasColumnType("uuid");

            entity.HasIndex(x => new { x.ElectionId, x.OccurredAt });
            entity.HasIndex(x => new { x.ElectionId, x.OrganizationVoterId, x.OccurredAt });
            entity.HasIndex(x => new { x.ElectionId, x.Outcome });
        });
    }

    private static void ConfigureElectionParticipation(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionParticipationRecord>(entity =>
        {
            entity.ToTable("ElectionParticipationRecord", "Elections");
            entity.HasKey(x => new { x.ElectionId, x.OrganizationVoterId });

            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.OrganizationVoterId).HasColumnType("varchar(128)");
            entity.Property(x => x.ParticipationStatus).HasConversion<string>().HasColumnType("varchar(24)");
            entity.Property(x => x.RecordedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.LastUpdatedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.LatestTransactionId).HasColumnType("uuid");
            entity.Property(x => x.LatestBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.LatestBlockId).HasColumnType("uuid");

            entity.HasIndex(x => new { x.ElectionId, x.ParticipationStatus });
            entity.HasIndex(x => new { x.ElectionId, x.LastUpdatedAt });
        });
    }

    private static void ConfigureElectionCommitmentRegistration(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionCommitmentRegistrationRecord>(entity =>
        {
            entity.ToTable("ElectionCommitmentRegistrationRecord", "Elections");
            entity.HasKey(x => new { x.ElectionId, x.OrganizationVoterId });

            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.OrganizationVoterId).HasColumnType("varchar(128)");
            entity.Property(x => x.LinkedActorPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.CommitmentHash).HasColumnType("varchar(256)");
            entity.Property(x => x.RegisteredAt).HasColumnType("timestamp with time zone");

            entity.HasIndex(x => new { x.ElectionId, x.LinkedActorPublicAddress });
            entity.HasIndex(x => new { x.ElectionId, x.CommitmentHash }).IsUnique();
            entity.HasIndex(x => new { x.ElectionId, x.RegisteredAt });
        });
    }

    private static void ConfigureElectionCheckoffConsumption(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionCheckoffConsumptionRecord>(entity =>
        {
            entity.ToTable("ElectionCheckoffConsumptionRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.OrganizationVoterId).HasColumnType("varchar(128)");
            entity.Property(x => x.ParticipationStatus).HasConversion<string>().HasColumnType("varchar(24)");
            entity.Property(x => x.ConsumedAt).HasColumnType("timestamp with time zone");

            entity.HasIndex(x => new { x.ElectionId, x.OrganizationVoterId }).IsUnique();
            entity.HasIndex(x => new { x.ElectionId, x.ConsumedAt });
        });
    }

    private static void ConfigureElectionEligibilitySnapshot(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionEligibilitySnapshotRecord>(entity =>
        {
            entity.ToTable("ElectionEligibilitySnapshotRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.SnapshotType).HasConversion<string>().HasColumnType("varchar(16)");
            entity.Property(x => x.EligibilityMutationPolicy).HasConversion<string>().HasColumnType("varchar(96)");
            entity.Property(x => x.RosteredCount).HasColumnType("integer");
            entity.Property(x => x.LinkedCount).HasColumnType("integer");
            entity.Property(x => x.ActiveDenominatorCount).HasColumnType("integer");
            entity.Property(x => x.CountedParticipationCount).HasColumnType("integer");
            entity.Property(x => x.BlankCount).HasColumnType("integer");
            entity.Property(x => x.DidNotVoteCount).HasColumnType("integer");
            entity.Property(x => x.RosteredVoterSetHash).HasColumnType("bytea");
            entity.Property(x => x.ActiveDenominatorSetHash).HasColumnType("bytea");
            entity.Property(x => x.CountedParticipationSetHash).HasColumnType("bytea");
            entity.Property(x => x.BoundaryArtifactId).HasColumnType("uuid");
            entity.Property(x => x.RecordedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.RecordedByPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.SourceTransactionId).HasColumnType("uuid");
            entity.Property(x => x.SourceBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.SourceBlockId).HasColumnType("uuid");

            entity.HasIndex(x => new { x.ElectionId, x.SnapshotType }).IsUnique();
            entity.HasIndex(x => new { x.ElectionId, x.RecordedAt });
        });
    }

    private static void ConfigureElectionRosterImportEvidence(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionRosterImportEvidenceRecord>(entity =>
        {
            entity.ToTable("ElectionRosterImportEvidenceRecord", "Elections");
            entity.HasKey(x => x.RosterImportId);

            entity.Property(x => x.RosterImportId).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.RosterImportVersion).HasColumnType("integer");
            entity.Property(x => x.RosterSourceFileHash).HasColumnType("varchar(256)");
            entity.Property(x => x.RosterCanonicalHash).HasColumnType("varchar(256)");
            entity.Property(x => x.RosterCanonicalizationVersion).HasColumnType("varchar(128)");
            entity.Property(x => x.RosterCanonicalizationVersionHash).HasColumnType("varchar(256)");
            entity.Property(x => x.AcceptedRowCount).HasColumnType("integer");
            entity.Property(x => x.RejectedRowCount).HasColumnType("integer");
            entity.Property(x => x.InvalidRowRejectionCount).HasColumnType("integer");
            entity.Property(x => x.DuplicateIdRejectionCount).HasColumnType("integer");
            entity.Property(x => x.DuplicateContactWarningCount).HasColumnType("integer");
            entity.Property(x => x.ImportedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.ImportedByActor).HasColumnType("varchar(160)");

            ConfigureJsonProperty(entity.Property(x => x.RejectedRows));
            ConfigureJsonProperty(entity.Property(x => x.DuplicateContactWarnings));

            entity.HasIndex(x => new { x.ElectionId, x.RosterImportVersion }).IsUnique();
            entity.HasIndex(x => new { x.ElectionId, x.ImportedAt });
            entity.HasIndex(x => new { x.ElectionId, x.RosterCanonicalHash });
        });
    }

    private static void ConfigureElectionEligibilityPolicyEvidence(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionEligibilityPolicyEvidenceRecord>(entity =>
        {
            entity.ToTable("ElectionEligibilityPolicyEvidenceRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.EligibilityPolicyId).HasColumnType("varchar(128)");
            entity.Property(x => x.EligibilityPolicyVersion).HasColumnType("varchar(64)");
            entity.Property(x => x.EligibilityMutationPolicy).HasConversion<string>().HasColumnType("varchar(96)");
            entity.Property(x => x.IdentityLinkPolicy).HasConversion<string>().HasColumnType("varchar(96)");
            entity.Property(x => x.CheckoffVisibilityPolicy).HasConversion<string>().HasColumnType("varchar(96)");
            entity.Property(x => x.ActorLinkMultiplicityPolicy).HasConversion<string>().HasColumnType("varchar(96)");
            entity.Property(x => x.ContactCodeProviderReadiness).HasConversion<string>().HasColumnType("varchar(40)");
            entity.Property(x => x.EligibilityPolicyCanonicalizationVersion).HasColumnType("varchar(128)");
            entity.Property(x => x.EligibilityPolicyCanonicalizationVersionHash).HasColumnType("varchar(256)");
            entity.Property(x => x.DeclaredAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.DeclaredByActor).HasColumnType("varchar(160)");
            entity.Property(x => x.SourceTransactionId).HasColumnType("uuid");
            entity.Property(x => x.SourceBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.SourceBlockId).HasColumnType("uuid");

            entity.HasIndex(x => new { x.ElectionId, x.DeclaredAt });
            entity.HasIndex(x => new { x.ElectionId, x.EligibilityPolicyId });
        });
    }

    private static void ConfigureElectionCommitmentSchemeEvidence(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionCommitmentSchemeEvidenceRecord>(entity =>
        {
            entity.ToTable("ElectionCommitmentSchemeEvidenceRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.CommitmentSchemeVersion).HasColumnType("varchar(128)");
            entity.Property(x => x.CommitmentSchemeVersionHash).HasColumnType("varchar(256)");
            entity.Property(x => x.NullifierSchemeVersion).HasColumnType("varchar(128)");
            entity.Property(x => x.NullifierSchemeVersionHash).HasColumnType("varchar(256)");
            entity.Property(x => x.RosterCanonicalizationVersion).HasColumnType("varchar(128)");
            entity.Property(x => x.RosterCanonicalizationVersionHash).HasColumnType("varchar(256)");
            entity.Property(x => x.EligibilityPolicyCanonicalizationVersion).HasColumnType("varchar(128)");
            entity.Property(x => x.EligibilityPolicyCanonicalizationVersionHash).HasColumnType("varchar(256)");
            entity.Property(x => x.DeclaredAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.DeclaredByActor).HasColumnType("varchar(160)");
            entity.Property(x => x.SourceTransactionId).HasColumnType("uuid");
            entity.Property(x => x.SourceBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.SourceBlockId).HasColumnType("uuid");

            entity.HasIndex(x => new { x.ElectionId, x.DeclaredAt });
            entity.HasIndex(x => new { x.ElectionId, x.CommitmentSchemeVersion });
        });
    }

    private static void ConfigureElectionAcceptedBallot(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionAcceptedBallotRecord>(entity =>
        {
            entity.ToTable("ElectionAcceptedBallotRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.EncryptedBallotPackage).HasColumnType("text");
            entity.Property(x => x.ProofBundle).HasColumnType("text");
            entity.Property(x => x.BallotNullifier).HasColumnType("varchar(256)");
            entity.Property(x => x.AcceptedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.PreparedBallotId).HasColumnType("uuid");
            entity.Property(x => x.PreparedBallotHash).HasColumnType("varchar(256)");
            entity.Property(x => x.ReceiptCommitment).HasColumnType("varchar(256)");
            entity.Property(x => x.ReceiptCommitmentScheme).HasColumnType("varchar(160)");
            entity.Property(x => x.BallotDefinitionVersion).HasColumnType("integer");
            entity.Property(x => x.BallotDefinitionHash).HasColumnType("bytea");

            entity.HasIndex(x => new { x.ElectionId, x.BallotNullifier }).IsUnique();
            entity.HasIndex(x => new { x.ElectionId, x.PreparedBallotId }).IsUnique();
            entity.HasIndex(x => new { x.ElectionId, x.ReceiptCommitment }).IsUnique();
            entity.HasIndex(x => new { x.ElectionId, x.AcceptedAt });
        });
    }

    private static void ConfigureElectionBallotMemPool(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionBallotMemPoolRecord>(entity =>
        {
            entity.ToTable("ElectionBallotMemPoolRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.AcceptedBallotId).HasColumnType("uuid");
            entity.Property(x => x.QueuedAt).HasColumnType("timestamp with time zone");

            entity.HasIndex(x => new { x.ElectionId, x.AcceptedBallotId }).IsUnique();
            entity.HasIndex(x => new { x.ElectionId, x.QueuedAt });
        });
    }

    private static void ConfigureElectionPublishedBallot(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionPublishedBallotRecord>(entity =>
        {
            entity.ToTable("ElectionPublishedBallotRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.PublicationSequence).HasColumnType("bigint");
            entity.Property(x => x.EncryptedBallotPackage).HasColumnType("text");
            entity.Property(x => x.ProofBundle).HasColumnType("text");
            entity.Property(x => x.PublishedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.SourceBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.SourceBlockId).HasColumnType("uuid");

            entity.HasIndex(x => new { x.ElectionId, x.PublicationSequence }).IsUnique();
            entity.HasIndex(x => new { x.ElectionId, x.PublishedAt });
        });
    }

    private static void ConfigureElectionCastIdempotency(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionCastIdempotencyRecord>(entity =>
        {
            entity.ToTable("ElectionCastIdempotencyRecord", "Elections");
            entity.HasKey(x => new { x.ElectionId, x.IdempotencyKeyHash });

            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.IdempotencyKeyHash).HasColumnType("varchar(256)");
            entity.Property(x => x.RecordedAt).HasColumnType("timestamp with time zone");

            entity.HasIndex(x => new { x.ElectionId, x.RecordedAt });
        });
    }

    private static void ConfigureElectionPublicationIssue(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionPublicationIssueRecord>(entity =>
        {
            entity.ToTable("ElectionPublicationIssueRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.IssueCode).HasConversion<string>().HasColumnType("varchar(64)");
            entity.Property(x => x.OccurrenceCount).HasColumnType("integer");
            entity.Property(x => x.FirstObservedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.LastObservedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.LatestBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.LatestBlockId).HasColumnType("uuid");

            entity.HasIndex(x => new { x.ElectionId, x.IssueCode }).IsUnique();
        });
    }

    private static void ConfigureElectionPublicationWitness(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionPublicationWitnessRecord>(entity =>
        {
            entity.ToTable("ElectionPublicationWitnessRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.WitnessSetId).HasColumnType("uuid");
            entity.Property(x => x.AcceptedBallotId).HasColumnType("uuid");
            entity.Property(x => x.PublishedSequence).HasColumnType("bigint");
            entity.Property(x => x.AcceptedEncryptedBallotHash).HasColumnType("varchar(128)");
            entity.Property(x => x.PublishedEncryptedBallotHash).HasColumnType("varchar(128)");
            entity.Property(x => x.ProofMode).HasColumnType("varchar(96)");
            entity.Property(x => x.ProofConstruction).HasColumnType("varchar(128)");
            entity.Property(x => x.StatementId).HasColumnType("varchar(128)");
            entity.Property(x => x.ProofProfileVersion).HasColumnType("varchar(64)");
            entity.Property(x => x.SealedWitnessMaterial).HasColumnType("text");
            entity.Property(x => x.SealedWitnessMaterialHash).HasColumnType("varchar(128)");
            entity.Property(x => x.SealAlgorithm).HasColumnType("varchar(96)");
            entity.Property(x => x.CustodyStatus).HasConversion<string>().HasColumnType("varchar(32)");
            entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.DeletedAt).HasColumnType("timestamp with time zone");

            entity.HasIndex(x => new { x.ElectionId, x.WitnessSetId });
            entity.HasIndex(x => new { x.ElectionId, x.AcceptedBallotId }).IsUnique();
            entity.HasIndex(x => new { x.ElectionId, x.CustodyStatus });
        });
    }

    private static void ConfigureElectionPublicationProofSession(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionPublicationProofSessionRecord>(entity =>
        {
            entity.ToTable("ElectionPublicationProofSessionRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.WitnessSetId).HasColumnType("uuid");
            entity.Property(x => x.ProofMode).HasColumnType("varchar(96)");
            entity.Property(x => x.ProofConstruction).HasColumnType("varchar(128)");
            entity.Property(x => x.StatementId).HasColumnType("varchar(128)");
            entity.Property(x => x.Status).HasConversion<string>().HasColumnType("varchar(32)");
            entity.Property(x => x.StartedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.CompletedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.AcceptedBallotCount).HasColumnType("integer");
            entity.Property(x => x.PublishedBallotCount).HasColumnType("integer");
            entity.Property(x => x.ChunkCount).HasColumnType("integer");
            entity.Property(x => x.RetryCount).HasColumnType("integer");
            entity.Property(x => x.FailureCode).HasColumnType("varchar(128)");
            entity.Property(x => x.FailureReason).HasColumnType("text");
            entity.Property(x => x.AcceptedBallotSetHash).HasColumnType("varchar(128)");
            entity.Property(x => x.PublishedBallotStreamHash).HasColumnType("varchar(128)");
            entity.Property(x => x.TranscriptHash).HasColumnType("varchar(128)");
            entity.Property(x => x.ProofHash).HasColumnType("varchar(128)");
            entity.Property(x => x.ServerVerifierOutputHash).HasColumnType("varchar(128)");
            entity.Property(x => x.DeletionReceiptId).HasColumnType("uuid");

            entity.HasIndex(x => new { x.ElectionId, x.StartedAt });
            entity.HasIndex(x => new { x.ElectionId, x.Status });
            entity.HasIndex(x => new { x.ElectionId, x.WitnessSetId });
        });
    }

    private static void ConfigureElectionPublicationProofTranscript(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionPublicationProofTranscriptRecord>(entity =>
        {
            entity.ToTable("ElectionPublicationProofTranscriptRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.ProofSessionId).HasColumnType("uuid");
            entity.Property(x => x.WitnessSetId).HasColumnType("uuid");
            entity.Property(x => x.TranscriptVersion).HasColumnType("varchar(96)");
            entity.Property(x => x.ProofMode).HasColumnType("varchar(96)");
            entity.Property(x => x.ProofConstruction).HasColumnType("varchar(128)");
            entity.Property(x => x.StatementId).HasColumnType("varchar(128)");
            entity.Property(x => x.ProfileId).HasColumnType("varchar(96)");
            entity.Property(x => x.BallotDefinitionHash).HasColumnType("varchar(128)");
            entity.Property(x => x.BallotEncryptionSchemeVersion).HasColumnType("varchar(96)");
            entity.Property(x => x.ElectionPublicKeyId).HasColumnType("varchar(128)");
            entity.Property(x => x.AcceptedBallotSetHash).HasColumnType("varchar(128)");
            entity.Property(x => x.PublishedBallotStreamHash).HasColumnType("varchar(128)");
            entity.Property(x => x.AcceptedBallotCount).HasColumnType("integer");
            entity.Property(x => x.PublishedBallotCount).HasColumnType("integer");
            entity.Property(x => x.CiphertextSlotCount).HasColumnType("integer");
            entity.Property(x => x.ProofSystemVersion).HasColumnType("varchar(128)");
            entity.Property(x => x.ProofBytes).HasColumnType("text");
            entity.Property(x => x.ProofHash).HasColumnType("varchar(128)");
            entity.Property(x => x.TranscriptHash).HasColumnType("varchar(128)");
            entity.Property(x => x.ExternalReviewStatus).HasColumnType("varchar(64)");
            entity.Property(x => x.GeneratedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.GeneratorReleaseHash).HasColumnType("varchar(128)");
            entity.Property(x => x.VerifierReleaseHash).HasColumnType("varchar(128)");
            entity.Property(x => x.StatementHashSha512).HasColumnType("varchar(128)");
            entity.Property(x => x.FiatShamirTranscriptHashSha512).HasColumnType("varchar(128)");
            entity.Property(x => x.CanonicalProofBytesHex).HasColumnType("text");
            entity.Property(x => x.CanonicalProofHashSha512).HasColumnType("varchar(128)");
            entity.Property(x => x.CanonicalProofByteLength).HasColumnType("integer");

            ConfigureJsonProperty(entity.Property(x => x.PublicPrivacyBoundary));

            entity.HasIndex(x => x.ProofSessionId).IsUnique();
            entity.HasIndex(x => new { x.ElectionId, x.GeneratedAt });
            entity.HasIndex(x => new { x.ElectionId, x.TranscriptHash });
        });
    }

    private static void ConfigureElectionPublicationWitnessDeletionReceipt(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionPublicationWitnessDeletionReceiptRecord>(entity =>
        {
            entity.ToTable("ElectionPublicationWitnessDeletionReceiptRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.ProofSessionId).HasColumnType("uuid");
            entity.Property(x => x.WitnessSetId).HasColumnType("uuid");
            entity.Property(x => x.WitnessSetHash).HasColumnType("varchar(128)");
            entity.Property(x => x.WitnessCount).HasColumnType("integer");
            entity.Property(x => x.TranscriptHash).HasColumnType("varchar(128)");
            entity.Property(x => x.ProofHash).HasColumnType("varchar(128)");
            entity.Property(x => x.DeletionStatus).HasConversion<string>().HasColumnType("varchar(32)");
            entity.Property(x => x.DeletedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.DeletionActorRef).HasColumnType("varchar(160)");
            entity.Property(x => x.FailureCode).HasColumnType("varchar(128)");
            entity.Property(x => x.FailureReason).HasColumnType("text");

            entity.HasIndex(x => x.ProofSessionId).IsUnique();
            entity.HasIndex(x => new { x.ElectionId, x.DeletedAt });
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
            entity.Property(x => x.TallyPublicKey).HasColumnType("bytea");

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
            entity.Property(x => x.CloseCountingPublicCommitment).HasColumnType("bytea");

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

    private static void ConfigureElectionFinalizationSession(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionFinalizationSessionRecord>(entity =>
        {
            entity.ToTable("ElectionFinalizationSessionRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.GovernedProposalId).HasColumnType("uuid");
            entity.Property(x => x.GovernanceMode).HasConversion<string>().HasColumnType("varchar(32)");
            entity.Property(x => x.SessionPurpose).HasConversion<string>().HasColumnType("varchar(32)");
            entity.Property(x => x.CloseArtifactId).HasColumnType("uuid");
            entity.Property(x => x.AcceptedBallotSetHash).HasColumnType("bytea");
            entity.Property(x => x.FinalEncryptedTallyHash).HasColumnType("bytea");
            entity.Property(x => x.TargetTallyId).HasColumnType("varchar(256)");
            entity.Property(x => x.RequiredShareCount).HasColumnType("integer");
            entity.Property(x => x.Status).HasConversion<string>().HasColumnType("varchar(32)");
            entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.CreatedByPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.CompletedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.ReleaseEvidenceId).HasColumnType("uuid");
            entity.Property(x => x.LatestTransactionId).HasColumnType("uuid");
            entity.Property(x => x.LatestBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.LatestBlockId).HasColumnType("uuid");

            ConfigureJsonProperty(entity.Property(x => x.CeremonySnapshot));
            ConfigureJsonProperty(entity.Property(x => x.EligibleTrustees));

            entity.HasIndex(x => new { x.ElectionId, x.CloseArtifactId }).IsUnique();
            entity.HasIndex(x => new { x.ElectionId, x.Status });
        });
    }

    private static void ConfigureElectionFinalizationShare(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionFinalizationShareRecord>(entity =>
        {
            entity.ToTable("ElectionFinalizationShareRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.FinalizationSessionId).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.TrusteeUserAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.TrusteeDisplayName).HasColumnType("varchar(200)");
            entity.Property(x => x.SubmittedByPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.ShareIndex).HasColumnType("integer");
            entity.Property(x => x.ShareVersion).HasColumnType("varchar(128)");
            entity.Property(x => x.TargetType).HasConversion<string>().HasColumnType("varchar(32)");
            entity.Property(x => x.ClaimedCloseArtifactId).HasColumnType("uuid");
            entity.Property(x => x.ClaimedAcceptedBallotSetHash).HasColumnType("bytea");
            entity.Property(x => x.ClaimedFinalEncryptedTallyHash).HasColumnType("bytea");
            entity.Property(x => x.ClaimedTargetTallyId).HasColumnType("varchar(256)");
            entity.Property(x => x.ClaimedCeremonyVersionId).HasColumnType("uuid");
            entity.Property(x => x.ClaimedTallyPublicKeyFingerprint).HasColumnType("varchar(256)");
            entity.Property(x => x.CloseCountingJobId).HasColumnType("uuid");
            entity.Property(x => x.ExecutorKeyAlgorithm).HasColumnType("varchar(64)");
            entity.Property(x => x.ShareMaterial).HasColumnType("text");
            entity.Property(x => x.ShareMaterialHash).HasColumnType("varchar(128)");
            entity.Property(x => x.Status).HasConversion<string>().HasColumnType("varchar(32)");
            entity.Property(x => x.FailureCode).HasColumnType("varchar(128)");
            entity.Property(x => x.FailureReason).HasColumnType("text");
            entity.Property(x => x.SubmittedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.SourceTransactionId).HasColumnType("uuid");
            entity.Property(x => x.SourceBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.SourceBlockId).HasColumnType("uuid");

            entity.HasIndex(x => new { x.FinalizationSessionId, x.SubmittedAt });
            entity.HasIndex(x => new { x.FinalizationSessionId, x.TrusteeUserAddress, x.Status });
        });
    }

    private static void ConfigureElectionCloseCountingJob(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionCloseCountingJobRecord>(entity =>
        {
            entity.ToTable("ElectionCloseCountingJobRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.FinalizationSessionId).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.CloseArtifactId).HasColumnType("uuid");
            entity.Property(x => x.AcceptedBallotSetHash).HasColumnType("bytea");
            entity.Property(x => x.FinalEncryptedTallyHash).HasColumnType("bytea");
            entity.Property(x => x.TargetTallyId).HasColumnType("varchar(256)");
            entity.Property(x => x.CeremonyVersionId).HasColumnType("uuid");
            entity.Property(x => x.TallyPublicKeyFingerprint).HasColumnType("varchar(256)");
            entity.Property(x => x.RequiredShareCount).HasColumnType("integer");
            entity.Property(x => x.Status).HasConversion<string>().HasColumnType("varchar(32)");
            entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.ThresholdReachedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.CompletedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.LastUpdatedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.RetryCount).HasColumnType("integer");
            entity.Property(x => x.FailureCode).HasColumnType("varchar(128)");
            entity.Property(x => x.FailureReason).HasColumnType("text");
            entity.Property(x => x.LatestTransactionId).HasColumnType("uuid");
            entity.Property(x => x.LatestBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.LatestBlockId).HasColumnType("uuid");

            entity.HasIndex(x => x.FinalizationSessionId).IsUnique();
            entity.HasIndex(x => new { x.ElectionId, x.Status });
            entity.HasIndex(x => new { x.ElectionId, x.CreatedAt });
        });
    }

    private static void ConfigureElectionExecutorSessionKeyEnvelope(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionExecutorSessionKeyEnvelopeRecord>(entity =>
        {
            entity.ToTable("ElectionExecutorSessionKeyEnvelopeRecord", "Elections");
            entity.HasKey(x => x.CloseCountingJobId);

            entity.Property(x => x.CloseCountingJobId).HasColumnType("uuid");
            entity.Property(x => x.ExecutorSessionPublicKey).HasColumnType("text");
            entity.Property(x => x.SealedExecutorSessionPrivateKey).HasColumnType("text");
            entity.Property(x => x.KeyAlgorithm).HasColumnType("varchar(96)");
            entity.Property(x => x.SealAlgorithm).HasColumnType("varchar(96)");
            entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.ExpiresAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.DestroyedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.SealedByServiceIdentity).HasColumnType("varchar(160)");
            entity.Property(x => x.LastUpdatedAt).HasColumnType("timestamp with time zone");
        });
    }

    private static void ConfigureAdminOnlyProtectedTallyEnvelope(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionAdminOnlyProtectedTallyEnvelopeRecord>(entity =>
        {
            entity.ToTable("ElectionAdminOnlyProtectedTallyEnvelopeRecord", "Elections");
            entity.HasKey(x => x.ElectionId);

            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.SelectedProfileId).HasColumnType("varchar(96)");
            entity.Property(x => x.TallyPublicKey).HasColumnType("bytea");
            entity.Property(x => x.TallyPublicKeyFingerprint).HasColumnType("varchar(256)");
            entity.Property(x => x.SealedTallyPrivateScalar).HasColumnType("text");
            entity.Property(x => x.ScalarEncoding).HasColumnType("varchar(96)");
            entity.Property(x => x.SealAlgorithm).HasColumnType("varchar(96)");
            entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.DestroyedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.SealedByServiceIdentity).HasColumnType("varchar(160)");
            entity.Property(x => x.LastUpdatedAt).HasColumnType("timestamp with time zone");
        });
    }

    private static void ConfigureElectionTallyExecutorLease(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionTallyExecutorLeaseRecord>(entity =>
        {
            entity.ToTable("ElectionTallyExecutorLeaseRecord", "Elections");
            entity.HasKey(x => x.CloseCountingJobId);

            entity.Property(x => x.CloseCountingJobId).HasColumnType("uuid");
            entity.Property(x => x.LeaseHolderId).HasColumnType("varchar(160)");
            entity.Property(x => x.LeaseEpoch).HasColumnType("bigint");
            entity.Property(x => x.LeasedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.LeaseExpiresAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.LastHeartbeatAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.ReleaseReason).HasColumnType("text");
            entity.Property(x => x.CompletionReason).HasColumnType("text");
        });
    }

    private static void ConfigureElectionFinalizationReleaseEvidence(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionFinalizationReleaseEvidenceRecord>(entity =>
        {
            entity.ToTable("ElectionFinalizationReleaseEvidenceRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.FinalizationSessionId).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.SessionPurpose).HasConversion<string>().HasColumnType("varchar(32)");
            entity.Property(x => x.ReleaseMode).HasConversion<string>().HasColumnType("varchar(32)");
            entity.Property(x => x.CloseArtifactId).HasColumnType("uuid");
            entity.Property(x => x.AcceptedBallotSetHash).HasColumnType("bytea");
            entity.Property(x => x.FinalEncryptedTallyHash).HasColumnType("bytea");
            entity.Property(x => x.TargetTallyId).HasColumnType("varchar(256)");
            entity.Property(x => x.AcceptedShareCount).HasColumnType("integer");
            entity.Property(x => x.CompletedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.CompletedByPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.SourceTransactionId).HasColumnType("uuid");
            entity.Property(x => x.SourceBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.SourceBlockId).HasColumnType("uuid");

            ConfigureJsonProperty(entity.Property(x => x.AcceptedTrustees));

            entity.HasIndex(x => x.FinalizationSessionId).IsUnique();
            entity.HasIndex(x => x.ElectionId);
        });
    }

    private static void ConfigureElectionAnomalyRecords(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ElectionAnomalyThreadRecord>(entity =>
        {
            entity.ToTable("ElectionAnomalyThreadRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.SubmitterPersonScopeId).HasColumnType("varchar(160)");
            entity.Property(x => x.SubmitterPersonScopeDerivationVersion).HasColumnType("varchar(96)");
            entity.Property(x => x.SubmitterActorPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.SubmitterRoleContextId).HasColumnType("varchar(64)");
            entity.Property(x => x.SubmitterRoleEvidenceTypeId).HasColumnType("varchar(96)");
            entity.Property(x => x.SubmitterRoleEvidenceReference).HasColumnType("text");
            entity.Property(x => x.LifecycleStateAtSubmission).HasConversion<string>().HasColumnType("varchar(32)");
            entity.Property(x => x.SubmissionWindowClosesAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.CurrentCategoryId).HasColumnType("varchar(96)");
            entity.Property(x => x.CurrentCaseStateId).HasColumnType("varchar(64)");
            entity.Property(x => x.SeverityCandidateId).HasColumnType("varchar(64)");
            entity.Property(x => x.GovernedDecisionRef).HasColumnType("text");
            entity.Property(x => x.HasOpenClarificationRequest).HasColumnType("boolean");
            entity.Property(x => x.OpenClarificationRequestId).HasColumnType("uuid");
            entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.LastUpdatedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.SourceTransactionId).HasColumnType("uuid");
            entity.Property(x => x.SourceBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.SourceBlockId).HasColumnType("uuid");
            entity.Property(x => x.CurrentThreadHash).HasColumnType("varchar(128)");

            entity.HasIndex(x => new { x.ElectionId, x.SubmitterPersonScopeId }).IsUnique();
            entity.HasIndex(x => new { x.ElectionId, x.CurrentCategoryId });
            entity.HasIndex(x => new { x.ElectionId, x.CurrentCaseStateId });
            entity.HasIndex(x => x.LastUpdatedAt);
            entity.HasIndex(x => x.SourceTransactionId);
        });

        modelBuilder.Entity<ElectionAnomalyThreadEventRecord>(entity =>
        {
            entity.ToTable("ElectionAnomalyThreadEventRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.AnomalyThreadId).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.Sequence).HasColumnType("integer");
            entity.Property(x => x.EventTypeId).HasColumnType("varchar(96)");
            entity.Property(x => x.EventPayloadJson).HasColumnType("jsonb");
            entity.Property(x => x.EventHash).HasColumnType("varchar(128)");
            entity.Property(x => x.PreviousEventHash).HasColumnType("varchar(128)");
            entity.Property(x => x.ActionNonce).HasColumnType("uuid");
            entity.Property(x => x.SourceTransactionId).HasColumnType("uuid");
            entity.Property(x => x.SourceBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.SourceBlockId).HasColumnType("uuid");
            entity.Property(x => x.ActorPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.OccurredAt).HasColumnType("timestamp with time zone");

            entity.HasIndex(x => new { x.AnomalyThreadId, x.Sequence }).IsUnique();
            entity.HasIndex(x => x.EventHash).IsUnique();
            entity.HasIndex(x => new { x.ElectionId, x.EventTypeId });
            entity.HasIndex(x => new { x.SourceTransactionId, x.ActionNonce }).IsUnique();
        });

        modelBuilder.Entity<ElectionAnomalyMessageEnvelopeRecord>(entity =>
        {
            entity.ToTable("ElectionAnomalyMessageEnvelopeRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.AnomalyThreadId).HasColumnType("uuid");
            entity.Property(x => x.EventId).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.MessageKindId).HasColumnType("varchar(96)");
            entity.Property(x => x.EncryptedBody).HasColumnType("text");
            entity.Property(x => x.EncryptedBodyHash).HasColumnType("varchar(128)");
            entity.Property(x => x.PlaintextBodyHash).HasColumnType("varchar(128)");
            entity.Property(x => x.PlaintextCharacterCount).HasColumnType("integer");
            entity.Property(x => x.EncryptionAlgorithm).HasColumnType("varchar(96)");
            entity.Property(x => x.RecordedAt).HasColumnType("timestamp with time zone");

            entity.HasIndex(x => x.EventId).IsUnique();
            entity.HasIndex(x => new { x.AnomalyThreadId, x.MessageKindId });
            entity.HasIndex(x => x.EncryptedBodyHash);
        });

        modelBuilder.Entity<ElectionAnomalyRecipientWrapRecord>(entity =>
        {
            entity.ToTable("ElectionAnomalyRecipientWrapRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.MessageEnvelopeId).HasColumnType("uuid");
            entity.Property(x => x.AnomalyThreadId).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.RecipientRoleId).HasColumnType("varchar(64)");
            entity.Property(x => x.RecipientPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.RecipientKeyFingerprint).HasColumnType("varchar(256)");
            entity.Property(x => x.EncryptedContentKey).HasColumnType("text");
            entity.Property(x => x.WrapAlgorithm).HasColumnType("varchar(96)");
            entity.Property(x => x.WrapStatusId).HasColumnType("varchar(64)");
            entity.Property(x => x.RecordedAt).HasColumnType("timestamp with time zone");

            entity.HasIndex(x => new { x.MessageEnvelopeId, x.RecipientRoleId, x.RecipientPublicAddress }).IsUnique();
            entity.HasIndex(x => new { x.AnomalyThreadId, x.RecipientRoleId });
            entity.HasIndex(x => new { x.ElectionId, x.WrapStatusId });
        });

        modelBuilder.Entity<ElectionAnomalyAttachmentManifestRecord>(entity =>
        {
            entity.ToTable("ElectionAnomalyAttachmentManifestRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.AnomalyThreadId).HasColumnType("uuid");
            entity.Property(x => x.EventId).HasColumnType("uuid");
            entity.Property(x => x.EventHash).HasColumnType("varchar(128)");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.AttachmentKindId).HasColumnType("varchar(96)");
            entity.Property(x => x.EncryptedPayloadReference).HasColumnType("varchar(160)");
            entity.Property(x => x.EncryptedPayloadHash).HasColumnType("varchar(128)");
            entity.Property(x => x.ContentHash).HasColumnType("varchar(128)");
            entity.Property(x => x.SizeBytes).HasColumnType("bigint");
            entity.Property(x => x.MimeType).HasColumnType("varchar(128)");
            entity.Property(x => x.ValidationStatusId).HasColumnType("varchar(64)");
            entity.Property(x => x.ScannerStatusId).HasColumnType("varchar(64)");
            entity.Property(x => x.PayloadAvailabilityStatusId).HasColumnType("varchar(64)");
            entity.Property(x => x.ClarificationRequestId).HasColumnType("uuid");
            entity.Property(x => x.ActorPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.ActorRoleId).HasColumnType("varchar(64)");
            entity.Property(x => x.SourceTransactionId).HasColumnType("uuid");
            entity.Property(x => x.SourceBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.SourceBlockId).HasColumnType("uuid");
            entity.Property(x => x.RecordedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.ContentKeyWrapsJson).HasColumnType("jsonb");

            entity.HasIndex(x => x.EventId).IsUnique();
            entity.HasIndex(x => x.SourceTransactionId).IsUnique();
            entity.HasIndex(x => x.EncryptedPayloadReference).IsUnique();
            entity.HasIndex(x => new { x.AnomalyThreadId, x.AttachmentKindId });
            entity.HasIndex(x => new { x.AnomalyThreadId, x.ClarificationRequestId });
            entity.HasIndex(x => new { x.ElectionId, x.ScannerStatusId });
            entity.HasIndex(x => new { x.ElectionId, x.PayloadAvailabilityStatusId });
        });

        modelBuilder.Entity<ElectionAnomalyEvidenceRedactionRecord>(entity =>
        {
            entity.ToTable("ElectionAnomalyEvidenceRedactionRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.AnomalyThreadId).HasColumnType("uuid");
            entity.Property(x => x.EventId).HasColumnType("uuid");
            entity.Property(x => x.EventHash).HasColumnType("varchar(128)");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.TargetKindId).HasColumnType("varchar(96)");
            entity.Property(x => x.TargetId).HasColumnType("varchar(160)");
            entity.Property(x => x.ReasonCodeId).HasColumnType("varchar(96)");
            entity.Property(x => x.OriginalHash).HasColumnType("varchar(128)");
            entity.Property(x => x.ReplacementManifestHash).HasColumnType("varchar(128)");
            entity.Property(x => x.TombstoneStatusId).HasColumnType("varchar(64)");
            entity.Property(x => x.HoldReference).HasColumnType("text");
            entity.Property(x => x.ActorPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.SourceTransactionId).HasColumnType("uuid");
            entity.Property(x => x.SourceBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.SourceBlockId).HasColumnType("uuid");
            entity.Property(x => x.RecordedAt).HasColumnType("timestamp with time zone");

            entity.HasIndex(x => x.EventId).IsUnique();
            entity.HasIndex(x => x.SourceTransactionId).IsUnique();
            entity.HasIndex(x => new { x.AnomalyThreadId, x.TargetKindId, x.TargetId });
            entity.HasIndex(x => new { x.ElectionId, x.ReasonCodeId });
        });

        modelBuilder.Entity<ElectionAnomalyRestrictedPayloadRecord>(entity =>
        {
            entity.ToTable("ElectionAnomalyRestrictedPayloadRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.AnomalyThreadId).HasColumnType("uuid");
            entity.Property(x => x.PayloadReference).HasColumnType("varchar(160)");
            entity.Property(x => x.EncryptedPayload).HasColumnType("bytea");
            entity.Property(x => x.EncryptedPayloadHash).HasColumnType("varchar(128)");
            entity.Property(x => x.ContentHash).HasColumnType("varchar(128)");
            entity.Property(x => x.SizeBytes).HasColumnType("bigint");
            entity.Property(x => x.MimeType).HasColumnType("varchar(128)");
            entity.Property(x => x.ScannerStatusId).HasColumnType("varchar(64)");
            entity.Property(x => x.PayloadAvailabilityStatusId).HasColumnType("varchar(64)");
            entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.LastCheckedAt).HasColumnType("timestamp with time zone");

            entity.HasIndex(x => x.PayloadReference).IsUnique();
            entity.HasIndex(x => new { x.AnomalyThreadId, x.PayloadAvailabilityStatusId });
            entity.HasIndex(x => new { x.ElectionId, x.ScannerStatusId });
        });

        modelBuilder.Entity<ElectionAnomalyActionRecord>(entity =>
        {
            entity.ToTable("ElectionAnomalyActionRecord", "Elections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.ElectionId)
                .HasConversion(
                    x => x.ToString(),
                    x => ElectionIdHandler.CreateFromString(x))
                .HasColumnType("varchar(40)");
            entity.Property(x => x.AnomalyThreadId).HasColumnType("uuid");
            entity.Property(x => x.ActionNonce).HasColumnType("uuid");
            entity.Property(x => x.ActionType).HasColumnType("varchar(96)");
            entity.Property(x => x.ActionOutcomeId).HasColumnType("varchar(64)");
            entity.Property(x => x.ActorPublicAddress).HasColumnType("varchar(160)");
            entity.Property(x => x.ValidationCode).HasColumnType("varchar(128)");
            entity.Property(x => x.DiagnosticReference).HasColumnType("text");
            entity.Property(x => x.SourceTransactionId).HasColumnType("uuid");
            entity.Property(x => x.SourceBlockHeight).HasColumnType("bigint");
            entity.Property(x => x.SourceBlockId).HasColumnType("uuid");
            entity.Property(x => x.RecordedAt).HasColumnType("timestamp with time zone");

            entity.HasIndex(x => x.SourceTransactionId).IsUnique();
            entity.HasIndex(x => new { x.ElectionId, x.ActionOutcomeId });
            entity.HasIndex(x => new { x.AnomalyThreadId, x.RecordedAt });
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
