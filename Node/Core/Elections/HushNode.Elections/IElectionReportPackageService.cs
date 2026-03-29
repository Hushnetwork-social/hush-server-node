using HushShared.Elections.Model;

namespace HushNode.Elections;

public interface IElectionReportPackageService
{
    ElectionReportPackageBuildResult Build(ElectionReportPackageBuildRequest request);
}

public sealed record ElectionReportPackageBuildRequest(
    ElectionRecord Election,
    ElectionBoundaryArtifactRecord CloseArtifact,
    ElectionBoundaryArtifactRecord TallyReadyArtifact,
    ElectionBoundaryArtifactRecord FinalizeArtifact,
    ElectionResultArtifactRecord UnofficialResult,
    ElectionResultArtifactRecord OfficialResult,
    ElectionEligibilitySnapshotRecord? CloseEligibilitySnapshot,
    ElectionFinalizationSessionRecord? FinalizationSession,
    ElectionFinalizationReleaseEvidenceRecord? FinalizationReleaseEvidence,
    IReadOnlyList<ElectionTrusteeInvitationRecord> TrusteeInvitations,
    IReadOnlyList<ElectionRosterEntryRecord> RosterEntries,
    IReadOnlyList<ElectionParticipationRecord> ParticipationRecords,
    int AttemptNumber,
    Guid? PreviousAttemptId,
    string AttemptedByPublicAddress,
    DateTime AttemptedAt);

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
