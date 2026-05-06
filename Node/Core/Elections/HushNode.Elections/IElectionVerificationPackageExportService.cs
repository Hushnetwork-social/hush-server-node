using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;

namespace HushNode.Elections;

public interface IElectionVerificationPackageExportService
{
    ElectionVerificationPackageExportResult Export(ElectionVerificationPackageExportRequest request);
}

public record ElectionVerificationPackageExportRequest(
    ElectionRecord Election,
    ProtocolPackageBindingRecord? ProtocolPackageBinding,
    ElectionReportPackageRecord? ReportPackage,
    IReadOnlyList<ElectionReportArtifactRecord> ReportArtifacts,
    IReadOnlyList<ElectionBoundaryArtifactRecord> BoundaryArtifacts,
    IReadOnlyList<ElectionAcceptedBallotRecord> AcceptedBallots,
    IReadOnlyList<ElectionPublishedBallotRecord> PublishedBallots,
    IReadOnlyList<ElectionFinalizationSessionRecord> FinalizationSessions,
    IReadOnlyList<ElectionFinalizationShareRecord> FinalizationShares,
    IReadOnlyList<ElectionFinalizationReleaseEvidenceRecord> ReleaseEvidenceRecords,
    IReadOnlyList<ElectionRosterEntryRecord> RosterEntries,
    IReadOnlyList<ElectionParticipationRecord> ParticipationRecords,
    VerificationPackageView PackageView,
    string VerifierProfileId,
    bool RestrictedAccessAuthorized,
    DateTime? ExportedAt = null,
    IReadOnlyList<ElectionVoterCeremonyRecord>? VoterCeremonyRecords = null,
    IReadOnlyList<ElectionPreparedBallotCommitmentRecord>? PreparedBallotCommitments = null,
    IReadOnlyList<ElectionSpoiledPreparedBallotRecord>? SpoiledPreparedBallots = null,
    IReadOnlyList<ElectionRosterImportEvidenceRecord>? RosterImportEvidences = null,
    IReadOnlyList<ElectionEligibilityPolicyEvidenceRecord>? EligibilityPolicyEvidences = null,
    IReadOnlyList<ElectionCommitmentSchemeEvidenceRecord>? CommitmentSchemeEvidences = null,
    IReadOnlyList<ElectionCommitmentRegistrationRecord>? CommitmentRegistrations = null,
    IReadOnlyList<ElectionCheckoffConsumptionRecord>? CheckoffConsumptions = null,
    IReadOnlyList<ElectionEligibilityActivationEventRecord>? EligibilityActivationEvents = null);

public record ElectionVerificationPackageExportResult(
    bool Success,
    string Code,
    string Message,
    string? PackageId,
    string? PackageHash,
    IReadOnlyList<ElectionVerificationPackageFile> Files);

public record ElectionVerificationPackageFile(
    string RelativePath,
    string MediaType,
    VerificationArtifactVisibility Visibility,
    byte[] Content)
{
    public string ContentText => System.Text.Encoding.UTF8.GetString(Content);
}

