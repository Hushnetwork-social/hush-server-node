namespace HushShared.Elections.Model;

public record ElectionCloseCountingJobRecord(
    Guid Id,
    Guid FinalizationSessionId,
    ElectionId ElectionId,
    Guid CloseArtifactId,
    byte[] AcceptedBallotSetHash,
    byte[] FinalEncryptedTallyHash,
    string TargetTallyId,
    Guid CeremonyVersionId,
    string TallyPublicKeyFingerprint,
    int RequiredShareCount,
    ElectionCloseCountingJobStatus Status,
    DateTime CreatedAt,
    DateTime? ThresholdReachedAt,
    DateTime? CompletedAt,
    DateTime LastUpdatedAt,
    int RetryCount,
    string? FailureCode,
    string? FailureReason,
    Guid? LatestTransactionId,
    long? LatestBlockHeight,
    Guid? LatestBlockId);

public record ElectionExecutorSessionKeyEnvelopeRecord(
    Guid CloseCountingJobId,
    string ExecutorSessionPublicKey,
    string SealedExecutorSessionPrivateKey,
    string KeyAlgorithm,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? DestroyedAt,
    string? SealedByServiceIdentity,
    DateTime LastUpdatedAt);

public record ElectionTallyExecutorLeaseRecord(
    Guid CloseCountingJobId,
    string LeaseHolderId,
    long LeaseEpoch,
    DateTime LeasedAt,
    DateTime LeaseExpiresAt,
    DateTime LastHeartbeatAt,
    string? ReleaseReason,
    string? CompletionReason);
