using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;

namespace HushNode.Elections;

public interface IElectionReportPackageService
{
    ElectionReportPackageBuildResult Build(ElectionReportPackageBuildRequest request);

    ElectionVoidReportPackageBuildResult BuildVoid(ElectionVoidReportPackageBuildRequest request);
}

public sealed record ElectionReportPackageBuildRequest(
    ElectionRecord Election,
    ElectionBoundaryArtifactRecord CloseArtifact,
    ElectionBoundaryArtifactRecord TallyReadyArtifact,
    ElectionBoundaryArtifactRecord FinalizeArtifact,
    ElectionResultArtifactRecord UnofficialResult,
    ElectionResultArtifactRecord OfficialResult,
    ElectionEligibilitySnapshotRecord? CloseEligibilitySnapshot,
    ProtocolPackageBindingRecord? ProtocolPackageBinding,
    ElectionFinalizationSessionRecord? FinalizationSession,
    ElectionFinalizationReleaseEvidenceRecord? FinalizationReleaseEvidence,
    ElectionGovernedProposalRecord? FinalizationGovernedProposal,
    IReadOnlyList<ElectionGovernedProposalApprovalRecord> FinalizationGovernedApprovals,
    IReadOnlyList<ElectionFinalizationShareRecord> FinalizationShares,
    IReadOnlyList<ElectionWarningAcknowledgementRecord> WarningAcknowledgements,
    IReadOnlyList<ElectionTrusteeInvitationRecord> TrusteeInvitations,
    IReadOnlyList<ElectionRosterEntryRecord> RosterEntries,
    IReadOnlyList<ElectionParticipationRecord> ParticipationRecords,
    int AttemptNumber,
    Guid? PreviousAttemptId,
    string AttemptedByPublicAddress,
    DateTime AttemptedAt,
    ElectionSp10OperationalSecurityStatusArtifactRecord? Sp10OperationalSecurityStatus = null,
    ElectionSp11RegulatoryClaimStateArtifactRecord? Sp11RegulatoryClaimState = null,
    AnomalyIntakeManifest? RestrictedAnomalyIntakeManifest = null);

public sealed record ElectionReportPackageBuildResult(
    bool IsSuccess,
    ElectionReportPackageRecord Package,
    IReadOnlyList<ElectionReportArtifactRecord> Artifacts,
    IReadOnlyList<ElectionReportAccessGrantRecord> AccessGrants)
{
    public static ElectionReportPackageBuildResult Success(
        ElectionReportPackageRecord package,
        IReadOnlyList<ElectionReportArtifactRecord> artifacts,
        IReadOnlyList<ElectionReportAccessGrantRecord>? accessGrants = null) =>
        new(
            true,
            package,
            artifacts,
            accessGrants ?? Array.Empty<ElectionReportAccessGrantRecord>());

    public static ElectionReportPackageBuildResult Failure(ElectionReportPackageRecord package) =>
        new(
            false,
            package,
            Array.Empty<ElectionReportArtifactRecord>(),
            Array.Empty<ElectionReportAccessGrantRecord>());
}

public sealed record ElectionVoidReportPackageBuildRequest(
    ElectionRecord Election,
    ElectionVoidDecisionRecord Decision,
    ElectionVoidPublicationAttemptRecord PublicationAttempt,
    IReadOnlyList<ElectionVoidSupersededArtifactRecord> SupersededArtifacts,
    ElectionResultArtifactRecord? HistoricalUnofficialResult,
    int AttemptNumber,
    Guid? PreviousReportPackageId,
    string AttemptedByPublicAddress,
    DateTime AttemptedAt);

public sealed record ElectionVoidReportPackageBuildResult(
    bool IsSuccess,
    ElectionReportPackageRecord Package,
    ElectionVoidPublicationAttemptRecord PublicationAttempt,
    ElectionVoidPublicStatusRecord? PublicStatus,
    ElectionVoidRestrictedEvidenceIndexRecord? RestrictedEvidenceIndex,
    IReadOnlyList<ElectionReportArtifactRecord> Artifacts)
{
    public static ElectionVoidReportPackageBuildResult Success(
        ElectionReportPackageRecord package,
        ElectionVoidPublicationAttemptRecord publicationAttempt,
        ElectionVoidPublicStatusRecord publicStatus,
        ElectionVoidRestrictedEvidenceIndexRecord restrictedEvidenceIndex,
        IReadOnlyList<ElectionReportArtifactRecord> artifacts) =>
        new(
            true,
            package,
            publicationAttempt,
            publicStatus,
            restrictedEvidenceIndex,
            artifacts);

    public static ElectionVoidReportPackageBuildResult Failure(
        ElectionReportPackageRecord package,
        ElectionVoidPublicationAttemptRecord publicationAttempt) =>
        new(
            false,
            package,
            publicationAttempt,
            PublicStatus: null,
            RestrictedEvidenceIndex: null,
            Array.Empty<ElectionReportArtifactRecord>());
}
