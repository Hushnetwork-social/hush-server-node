using HushShared.Elections.Model;
using Microsoft.EntityFrameworkCore;

namespace HushNode.Elections.Storage;

public class ElectionsDbContext(
    ElectionsDbContextConfigurator electionsDbContextConfigurator,
    DbContextOptions<ElectionsDbContext> options) : DbContext(options)
{
    private readonly ElectionsDbContextConfigurator _electionsDbContextConfigurator = electionsDbContextConfigurator;

    public DbSet<ElectionRecord> Elections { get; set; }
    public DbSet<ElectionDraftSnapshotRecord> ElectionDraftSnapshots { get; set; }
    public DbSet<ElectionEnvelopeAccessRecord> ElectionEnvelopeAccessRecords { get; set; }
    public DbSet<ElectionResultArtifactRecord> ElectionResultArtifacts { get; set; }
    public DbSet<ElectionReportPackageRecord> ElectionReportPackages { get; set; }
    public DbSet<ElectionReportArtifactRecord> ElectionReportArtifacts { get; set; }
    public DbSet<ElectionReportAccessGrantRecord> ElectionReportAccessGrants { get; set; }
    public DbSet<ElectionRosterEntryRecord> ElectionRosterEntries { get; set; }
    public DbSet<ElectionEligibilityActivationEventRecord> ElectionEligibilityActivationEvents { get; set; }
    public DbSet<ElectionParticipationRecord> ElectionParticipationRecords { get; set; }
    public DbSet<ElectionCommitmentRegistrationRecord> ElectionCommitmentRegistrations { get; set; }
    public DbSet<ElectionCheckoffConsumptionRecord> ElectionCheckoffConsumptions { get; set; }
    public DbSet<ElectionEligibilitySnapshotRecord> ElectionEligibilitySnapshots { get; set; }
    public DbSet<ElectionBoundaryArtifactRecord> ElectionBoundaryArtifacts { get; set; }
    public DbSet<ElectionAcceptedBallotRecord> ElectionAcceptedBallots { get; set; }
    public DbSet<ElectionBallotMemPoolRecord> ElectionBallotMemPoolEntries { get; set; }
    public DbSet<ElectionPublishedBallotRecord> ElectionPublishedBallots { get; set; }
    public DbSet<ElectionCastIdempotencyRecord> ElectionCastIdempotencyRecords { get; set; }
    public DbSet<ElectionPublicationIssueRecord> ElectionPublicationIssues { get; set; }
    public DbSet<ElectionWarningAcknowledgementRecord> ElectionWarningAcknowledgements { get; set; }
    public DbSet<ElectionTrusteeInvitationRecord> ElectionTrusteeInvitations { get; set; }
    public DbSet<ElectionGovernedProposalRecord> ElectionGovernedProposals { get; set; }
    public DbSet<ElectionGovernedProposalApprovalRecord> ElectionGovernedProposalApprovals { get; set; }
    public DbSet<ElectionCeremonyProfileRecord> ElectionCeremonyProfiles { get; set; }
    public DbSet<ElectionCeremonyVersionRecord> ElectionCeremonyVersions { get; set; }
    public DbSet<ElectionCeremonyTranscriptEventRecord> ElectionCeremonyTranscriptEvents { get; set; }
    public DbSet<ElectionCeremonyMessageEnvelopeRecord> ElectionCeremonyMessageEnvelopes { get; set; }
    public DbSet<ElectionCeremonyTrusteeStateRecord> ElectionCeremonyTrusteeStates { get; set; }
    public DbSet<ElectionCeremonyShareCustodyRecord> ElectionCeremonyShareCustodyRecords { get; set; }
    public DbSet<ElectionFinalizationSessionRecord> ElectionFinalizationSessions { get; set; }
    public DbSet<ElectionCloseCountingJobRecord> ElectionCloseCountingJobs { get; set; }
    public DbSet<ElectionExecutorSessionKeyEnvelopeRecord> ElectionExecutorSessionKeyEnvelopes { get; set; }
    public DbSet<ElectionAdminOnlyProtectedTallyEnvelopeRecord> ElectionAdminOnlyProtectedTallyEnvelopes { get; set; }
    public DbSet<ElectionTallyExecutorLeaseRecord> ElectionTallyExecutorLeases { get; set; }
    public DbSet<ElectionFinalizationShareRecord> ElectionFinalizationShares { get; set; }
    public DbSet<ElectionFinalizationReleaseEvidenceRecord> ElectionFinalizationReleaseEvidenceRecords { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        _electionsDbContextConfigurator.Configure(modelBuilder);
    }
}
