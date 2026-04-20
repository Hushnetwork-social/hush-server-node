namespace HushShared.Elections.Model;

public static partial class ElectionModelFactory
{
    public static ElectionCloseCountingJobRecord CreateCloseCountingJob(
        ElectionFinalizationSessionRecord session,
        DateTime? createdAt = null,
        Guid? latestTransactionId = null,
        long? latestBlockHeight = null,
        Guid? latestBlockId = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.SessionPurpose != ElectionFinalizationSessionPurpose.CloseCounting)
        {
            throw new ArgumentException("Close-counting jobs may only be created for close-counting sessions.", nameof(session));
        }

        if (session.CeremonySnapshot is null)
        {
            throw new ArgumentException("Close-counting jobs require a bound ceremony snapshot.", nameof(session));
        }

        var timestamp = createdAt ?? DateTime.UtcNow;
        return new ElectionCloseCountingJobRecord(
            Guid.NewGuid(),
            session.Id,
            session.ElectionId,
            session.CloseArtifactId,
            CloneBytes(session.AcceptedBallotSetHash)!,
            CloneBytes(session.FinalEncryptedTallyHash)!,
            NormalizeRequiredText(session.TargetTallyId, nameof(session)),
            session.CeremonySnapshot.CeremonyVersionId,
            NormalizeRequiredText(session.CeremonySnapshot.TallyPublicKeyFingerprint, nameof(session)),
            session.RequiredShareCount,
            ElectionCloseCountingJobStatus.AwaitingShares,
            timestamp,
            ThresholdReachedAt: null,
            CompletedAt: null,
            LastUpdatedAt: timestamp,
            RetryCount: 0,
            FailureCode: null,
            FailureReason: null,
            latestTransactionId,
            latestBlockHeight,
            latestBlockId);
    }

    public static ElectionExecutorSessionKeyEnvelopeRecord CreateExecutorSessionKeyEnvelope(
        Guid closeCountingJobId,
        string executorSessionPublicKey,
        string sealedExecutorSessionPrivateKey,
        string keyAlgorithm,
        string sealAlgorithm,
        DateTime? createdAt = null,
        DateTime? expiresAt = null,
        DateTime? destroyedAt = null,
        string? sealedByServiceIdentity = null)
    {
        var timestamp = createdAt ?? DateTime.UtcNow;
        return new ElectionExecutorSessionKeyEnvelopeRecord(
            closeCountingJobId,
            NormalizeRequiredText(executorSessionPublicKey, nameof(executorSessionPublicKey)),
            NormalizeRequiredText(sealedExecutorSessionPrivateKey, nameof(sealedExecutorSessionPrivateKey)),
            NormalizeRequiredText(keyAlgorithm, nameof(keyAlgorithm)),
            NormalizeRequiredText(sealAlgorithm, nameof(sealAlgorithm)),
            timestamp,
            expiresAt,
            destroyedAt,
            NormalizeOptionalText(sealedByServiceIdentity),
            timestamp);
    }

    public static ElectionTallyExecutorLeaseRecord CreateTallyExecutorLease(
        Guid closeCountingJobId,
        string leaseHolderId,
        long leaseEpoch,
        DateTime leasedAt,
        DateTime leaseExpiresAt,
        DateTime? lastHeartbeatAt = null,
        string? releaseReason = null,
        string? completionReason = null)
    {
        if (leaseEpoch < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseEpoch), "Lease epoch must be at least 1.");
        }

        if (leaseExpiresAt <= leasedAt)
        {
            throw new ArgumentException("Lease expiry must be later than the lease start.", nameof(leaseExpiresAt));
        }

        return new ElectionTallyExecutorLeaseRecord(
            closeCountingJobId,
            NormalizeRequiredText(leaseHolderId, nameof(leaseHolderId)),
            leaseEpoch,
            leasedAt,
            leaseExpiresAt,
            lastHeartbeatAt ?? leasedAt,
            NormalizeOptionalText(releaseReason),
            NormalizeOptionalText(completionReason));
    }
}
