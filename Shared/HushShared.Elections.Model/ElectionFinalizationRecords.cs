namespace HushShared.Elections.Model;

public record ElectionFinalizationSessionRecord(
    Guid Id,
    ElectionId ElectionId,
    Guid? GovernedProposalId,
    ElectionGovernanceMode GovernanceMode,
    Guid CloseArtifactId,
    byte[] AcceptedBallotSetHash,
    byte[] FinalEncryptedTallyHash,
    string TargetTallyId,
    ElectionCeremonyBindingSnapshot? CeremonySnapshot,
    int RequiredShareCount,
    IReadOnlyList<ElectionTrusteeReference> EligibleTrustees,
    ElectionFinalizationSessionStatus Status,
    DateTime CreatedAt,
    string CreatedByPublicAddress,
    DateTime? CompletedAt,
    Guid? ReleaseEvidenceId,
    Guid? LatestTransactionId,
    long? LatestBlockHeight,
    Guid? LatestBlockId)
{
    public ElectionFinalizationSessionRecord MarkCompleted(
        Guid releaseEvidenceId,
        DateTime completedAt,
        Guid? latestTransactionId = null,
        long? latestBlockHeight = null,
        Guid? latestBlockId = null)
    {
        if (Status == ElectionFinalizationSessionStatus.Completed)
        {
            throw new InvalidOperationException("Finalization session is already completed.");
        }

        return this with
        {
            Status = ElectionFinalizationSessionStatus.Completed,
            CompletedAt = completedAt,
            ReleaseEvidenceId = releaseEvidenceId,
            LatestTransactionId = latestTransactionId,
            LatestBlockHeight = latestBlockHeight,
            LatestBlockId = latestBlockId,
        };
    }
}

public record ElectionFinalizationShareRecord(
    Guid Id,
    Guid FinalizationSessionId,
    ElectionId ElectionId,
    string TrusteeUserAddress,
    string? TrusteeDisplayName,
    string SubmittedByPublicAddress,
    int ShareIndex,
    string ShareVersion,
    ElectionFinalizationTargetType TargetType,
    Guid ClaimedCloseArtifactId,
    byte[] ClaimedAcceptedBallotSetHash,
    byte[] ClaimedFinalEncryptedTallyHash,
    string ClaimedTargetTallyId,
    Guid? ClaimedCeremonyVersionId,
    string? ClaimedTallyPublicKeyFingerprint,
    string ShareMaterial,
    ElectionFinalizationShareStatus Status,
    string? FailureCode,
    string? FailureReason,
    DateTime SubmittedAt,
    Guid? SourceTransactionId,
    long? SourceBlockHeight,
    Guid? SourceBlockId)
{
    public bool IsAccepted => Status == ElectionFinalizationShareStatus.Accepted;
}

public record ElectionFinalizationReleaseEvidenceRecord(
    Guid Id,
    Guid FinalizationSessionId,
    ElectionId ElectionId,
    ElectionFinalizationReleaseMode ReleaseMode,
    Guid CloseArtifactId,
    byte[] AcceptedBallotSetHash,
    byte[] FinalEncryptedTallyHash,
    string TargetTallyId,
    int AcceptedShareCount,
    IReadOnlyList<ElectionTrusteeReference> AcceptedTrustees,
    DateTime CompletedAt,
    string CompletedByPublicAddress,
    Guid? SourceTransactionId,
    long? SourceBlockHeight,
    Guid? SourceBlockId);
