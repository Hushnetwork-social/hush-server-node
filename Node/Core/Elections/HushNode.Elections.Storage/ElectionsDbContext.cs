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
    public DbSet<ElectionBoundaryArtifactRecord> ElectionBoundaryArtifacts { get; set; }
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        _electionsDbContextConfigurator.Configure(modelBuilder);
    }
}
