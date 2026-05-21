using System.Security.Cryptography;
using System.Text;

namespace HushShared.Elections.Model;

public static partial class ElectionModelFactory
{
    public static ElectionVoidEvidenceReferenceRecord CreateVoidEvidenceReference(
        ElectionVoidEvidenceReferenceKind referenceKind,
        string referenceId,
        Guid? internalRecordId = null,
        string? externalReference = null,
        string? referenceHash = null,
        ElectionVoidEvidenceVisibility visibility = ElectionVoidEvidenceVisibility.RestrictedOwnerAuditor,
        DateTime? recordedAt = null,
        Guid? preassignedId = null) =>
        new(
            preassignedId ?? Guid.NewGuid(),
            referenceKind,
            referenceId,
            internalRecordId,
            externalReference,
            referenceHash,
            visibility,
            recordedAt ?? DateTime.UtcNow);

    public static ElectionVoidDecisionRecord CreateVoidDecision(
        ElectionRecord election,
        string actorPublicAddress,
        string publicJustification,
        Guid voidBoundaryArtifactId,
        IReadOnlyList<ElectionVoidEvidenceReferenceRecord>? evidenceReferences = null,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null,
        DateTime? decidedAt = null,
        Guid? preassignedDecisionId = null) =>
        new(
            preassignedDecisionId ?? Guid.NewGuid(),
            election.ElectionId,
            NormalizeRequiredText(actorPublicAddress, nameof(actorPublicAddress)),
            ElectionVoidDecisionRecord.ElectionOwnerRole,
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId,
            decidedAt ?? DateTime.UtcNow,
            election.LifecycleState,
            ElectionLifecycleState.Voided,
            publicJustification,
            HashPublicJustification(publicJustification),
            evidenceReferences ?? Array.Empty<ElectionVoidEvidenceReferenceRecord>(),
            voidBoundaryArtifactId,
            CurrentPublicationAttemptId: null,
            ElectionVoidPublicationAttemptStatus.Pending);

    public static ElectionVoidPublicationAttemptRecord CreateSealedVoidPublicationAttempt(
        ElectionId electionId,
        Guid voidDecisionId,
        int attemptNumber,
        byte[] frozenEvidenceHash,
        string frozenEvidenceFingerprint,
        byte[] packageHash,
        int artifactCount,
        string attemptedByPublicAddress,
        Guid? reportPackageId = null,
        Guid? previousAttemptId = null,
        string? publicStatusArtifactRef = null,
        string? voidPackageArtifactRef = null,
        DateTime? attemptedAt = null,
        DateTime? sealedAt = null,
        Guid? preassignedAttemptId = null)
    {
        if (artifactCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(artifactCount), "Artifact count must be at least 1.");
        }

        var attemptedTimestamp = attemptedAt ?? DateTime.UtcNow;

        return new ElectionVoidPublicationAttemptRecord(
            preassignedAttemptId ?? Guid.NewGuid(),
            electionId,
            voidDecisionId,
            attemptNumber,
            previousAttemptId,
            reportPackageId,
            ElectionVoidPublicationAttemptStatus.Sealed,
            CloneBytes(frozenEvidenceHash) ?? Array.Empty<byte>(),
            NormalizeRequiredText(frozenEvidenceFingerprint, nameof(frozenEvidenceFingerprint)),
            CloneBytes(packageHash),
            artifactCount,
            FailureCode: null,
            FailureReason: null,
            NormalizeOptionalText(publicStatusArtifactRef),
            NormalizeOptionalText(voidPackageArtifactRef),
            attemptedTimestamp,
            sealedAt ?? attemptedTimestamp,
            NormalizeRequiredText(attemptedByPublicAddress, nameof(attemptedByPublicAddress)));
    }

    public static ElectionVoidPublicationAttemptRecord CreateFailedVoidPublicationAttempt(
        ElectionId electionId,
        Guid voidDecisionId,
        int attemptNumber,
        byte[] frozenEvidenceHash,
        string frozenEvidenceFingerprint,
        string attemptedByPublicAddress,
        string failureCode,
        string failureReason,
        Guid? previousAttemptId = null,
        DateTime? attemptedAt = null,
        Guid? preassignedAttemptId = null) =>
        new(
            preassignedAttemptId ?? Guid.NewGuid(),
            electionId,
            voidDecisionId,
            attemptNumber,
            previousAttemptId,
            ReportPackageId: null,
            ElectionVoidPublicationAttemptStatus.GenerationFailed,
            CloneBytes(frozenEvidenceHash) ?? Array.Empty<byte>(),
            NormalizeRequiredText(frozenEvidenceFingerprint, nameof(frozenEvidenceFingerprint)),
            PackageHash: null,
            ArtifactCount: 0,
            NormalizeRequiredText(failureCode, nameof(failureCode)),
            NormalizeRequiredText(failureReason, nameof(failureReason)),
            PublicStatusArtifactRef: null,
            VoidPackageArtifactRef: null,
            attemptedAt ?? DateTime.UtcNow,
            SealedAt: null,
            NormalizeRequiredText(attemptedByPublicAddress, nameof(attemptedByPublicAddress)));

    public static ElectionVoidSupersededArtifactRecord CreateVoidSupersededArtifact(
        ElectionId electionId,
        Guid voidDecisionId,
        ElectionVoidSupersededArtifactKind artifactKind,
        string artifactRef,
        Guid? reportPackageId = null,
        Guid? reportArtifactId = null,
        string? artifactHash = null,
        DateTime? supersededAt = null,
        Guid? preassignedId = null) =>
        new(
            preassignedId ?? Guid.NewGuid(),
            electionId,
            voidDecisionId,
            artifactKind,
            reportPackageId,
            reportArtifactId,
            artifactRef,
            artifactHash,
            supersededAt ?? DateTime.UtcNow);

    private static byte[] HashPublicJustification(string publicJustification)
    {
        var normalized = ElectionVoidPublicJustificationValidator.NormalizeAndThrow(publicJustification);
        return SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
    }
}
