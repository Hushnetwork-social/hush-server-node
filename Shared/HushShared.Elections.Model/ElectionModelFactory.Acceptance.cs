namespace HushShared.Elections.Model;

public static partial class ElectionModelFactory
{
    public static ElectionCommitmentRegistrationRecord CreateCommitmentRegistrationRecord(
        ElectionId electionId,
        string organizationVoterId,
        string linkedActorPublicAddress,
        string commitmentHash,
        DateTime? registeredAt = null)
    {
        return new ElectionCommitmentRegistrationRecord(
            electionId,
            ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(organizationVoterId, nameof(organizationVoterId)),
            ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(linkedActorPublicAddress, nameof(linkedActorPublicAddress)),
            ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(commitmentHash, nameof(commitmentHash)),
            registeredAt ?? DateTime.UtcNow);
    }

    public static ElectionCheckoffConsumptionRecord CreateCheckoffConsumptionRecord(
        ElectionId electionId,
        string organizationVoterId,
        DateTime? consumedAt = null)
    {
        return new ElectionCheckoffConsumptionRecord(
            Guid.NewGuid(),
            electionId,
            organizationVoterId,
            ElectionParticipationStatus.CountedAsVoted,
            consumedAt ?? DateTime.UtcNow);
    }

    public static ElectionAcceptedBallotRecord CreateAcceptedBallotRecord(
        ElectionId electionId,
        string encryptedBallotPackage,
        string proofBundle,
        string ballotNullifier,
        DateTime? acceptedAt = null)
    {
        return new ElectionAcceptedBallotRecord(
            Guid.NewGuid(),
            electionId,
            ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(encryptedBallotPackage, nameof(encryptedBallotPackage)),
            ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(proofBundle, nameof(proofBundle)),
            ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(ballotNullifier, nameof(ballotNullifier)),
            acceptedAt ?? DateTime.UtcNow);
    }

    public static ElectionCastIdempotencyRecord CreateCastIdempotencyRecord(
        ElectionId electionId,
        string idempotencyKeyHash,
        DateTime? recordedAt = null)
    {
        return new ElectionCastIdempotencyRecord(
            electionId,
            ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(idempotencyKeyHash, nameof(idempotencyKeyHash)),
            recordedAt ?? DateTime.UtcNow);
    }
}
