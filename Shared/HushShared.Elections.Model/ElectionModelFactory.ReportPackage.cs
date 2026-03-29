namespace HushShared.Elections.Model;

public static partial class ElectionModelFactory
{
    public static ElectionReportPackageRecord CreateFailedReportPackageAttempt(
        ElectionId electionId,
        int attemptNumber,
        Guid tallyReadyArtifactId,
        Guid unofficialResultArtifactId,
        byte[] frozenEvidenceHash,
        string frozenEvidenceFingerprint,
        string attemptedByPublicAddress,
        string failureCode,
        string failureReason,
        Guid? previousAttemptId = null,
        Guid? finalizationSessionId = null,
        Guid? closeBoundaryArtifactId = null,
        Guid? closeEligibilitySnapshotId = null,
        Guid? finalizationReleaseEvidenceId = null,
        DateTime? attemptedAt = null,
        Guid? preassignedPackageId = null)
    {
        if (attemptNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber), "Attempt number must be at least 1.");
        }

        return new ElectionReportPackageRecord(
            preassignedPackageId ?? Guid.NewGuid(),
            electionId,
            attemptNumber,
            previousAttemptId,
            finalizationSessionId,
            tallyReadyArtifactId,
            unofficialResultArtifactId,
            OfficialResultArtifactId: null,
            FinalizeArtifactId: null,
            closeBoundaryArtifactId,
            closeEligibilitySnapshotId,
            finalizationReleaseEvidenceId,
            ElectionReportPackageStatus.GenerationFailed,
            CloneBytes(frozenEvidenceHash) ?? Array.Empty<byte>(),
            NormalizeRequiredText(frozenEvidenceFingerprint, nameof(frozenEvidenceFingerprint)),
            PackageHash: null,
            ArtifactCount: 0,
            FailureCode: NormalizeRequiredText(failureCode, nameof(failureCode)),
            FailureReason: NormalizeRequiredText(failureReason, nameof(failureReason)),
            AttemptedAt: attemptedAt ?? DateTime.UtcNow,
            SealedAt: null,
            AttemptedByPublicAddress: NormalizeRequiredText(attemptedByPublicAddress, nameof(attemptedByPublicAddress)));
    }

    public static ElectionReportPackageRecord CreateSealedReportPackage(
        ElectionId electionId,
        int attemptNumber,
        Guid tallyReadyArtifactId,
        Guid unofficialResultArtifactId,
        Guid officialResultArtifactId,
        Guid finalizeArtifactId,
        byte[] frozenEvidenceHash,
        string frozenEvidenceFingerprint,
        byte[] packageHash,
        int artifactCount,
        string attemptedByPublicAddress,
        Guid? previousAttemptId = null,
        Guid? finalizationSessionId = null,
        Guid? closeBoundaryArtifactId = null,
        Guid? closeEligibilitySnapshotId = null,
        Guid? finalizationReleaseEvidenceId = null,
        DateTime? attemptedAt = null,
        DateTime? sealedAt = null,
        Guid? preassignedPackageId = null)
    {
        if (attemptNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber), "Attempt number must be at least 1.");
        }

        if (artifactCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(artifactCount), "Artifact count must be at least 1.");
        }

        var attemptedTimestamp = attemptedAt ?? DateTime.UtcNow;
        var sealedTimestamp = sealedAt ?? attemptedTimestamp;

        return new ElectionReportPackageRecord(
            preassignedPackageId ?? Guid.NewGuid(),
            electionId,
            attemptNumber,
            previousAttemptId,
            finalizationSessionId,
            tallyReadyArtifactId,
            unofficialResultArtifactId,
            officialResultArtifactId,
            finalizeArtifactId,
            closeBoundaryArtifactId,
            closeEligibilitySnapshotId,
            finalizationReleaseEvidenceId,
            ElectionReportPackageStatus.Sealed,
            CloneBytes(frozenEvidenceHash) ?? Array.Empty<byte>(),
            NormalizeRequiredText(frozenEvidenceFingerprint, nameof(frozenEvidenceFingerprint)),
            CloneBytes(packageHash) ?? Array.Empty<byte>(),
            artifactCount,
            FailureCode: null,
            FailureReason: null,
            AttemptedAt: attemptedTimestamp,
            SealedAt: sealedTimestamp,
            AttemptedByPublicAddress: NormalizeRequiredText(attemptedByPublicAddress, nameof(attemptedByPublicAddress)));
    }

    public static ElectionReportArtifactRecord CreateReportArtifact(
        Guid reportPackageId,
        ElectionId electionId,
        ElectionReportArtifactKind artifactKind,
        ElectionReportArtifactFormat format,
        ElectionReportArtifactAccessScope accessScope,
        int sortOrder,
        string title,
        string fileName,
        string mediaType,
        byte[] contentHash,
        string content,
        Guid? pairedArtifactId = null,
        DateTime? recordedAt = null,
        Guid? preassignedArtifactId = null)
    {
        if (sortOrder < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sortOrder), "Sort order must be at least 1.");
        }

        return new ElectionReportArtifactRecord(
            preassignedArtifactId ?? Guid.NewGuid(),
            reportPackageId,
            electionId,
            artifactKind,
            format,
            accessScope,
            sortOrder,
            NormalizeRequiredText(title, nameof(title)),
            NormalizeRequiredText(fileName, nameof(fileName)),
            NormalizeRequiredText(mediaType, nameof(mediaType)),
            CloneBytes(contentHash) ?? Array.Empty<byte>(),
            content,
            pairedArtifactId,
            recordedAt ?? DateTime.UtcNow);
    }

    public static ElectionReportAccessGrantRecord CreateReportAccessGrant(
        ElectionId electionId,
        string actorPublicAddress,
        string grantedByPublicAddress,
        ElectionReportAccessGrantRole grantRole = ElectionReportAccessGrantRole.DesignatedAuditor,
        DateTime? grantedAt = null,
        Guid? preassignedGrantId = null) =>
        new(
            preassignedGrantId ?? Guid.NewGuid(),
            electionId,
            NormalizeRequiredText(actorPublicAddress, nameof(actorPublicAddress)),
            grantRole,
            grantedAt ?? DateTime.UtcNow,
            NormalizeRequiredText(grantedByPublicAddress, nameof(grantedByPublicAddress)));
}
