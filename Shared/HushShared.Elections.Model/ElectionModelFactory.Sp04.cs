namespace HushShared.Elections.Model;

public static partial class ElectionModelFactory
{
    public static ElectionBallotDefinitionSealRecord CreateBallotDefinitionSeal(
        int ballotDefinitionVersion,
        byte[] ballotDefinitionHash,
        DateTime? sealedAt = null,
        ElectionBallotDefinitionMutationPolicy mutationPolicy = ElectionBallotDefinitionMutationPolicy.ImmutableAfterOpen)
    {
        if (ballotDefinitionVersion < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ballotDefinitionVersion),
                "Ballot definition version must be at least 1.");
        }

        return new ElectionBallotDefinitionSealRecord(
            ballotDefinitionVersion,
            CloneBytes(ballotDefinitionHash)!,
            sealedAt ?? DateTime.UtcNow,
            mutationPolicy);
    }

    public static ElectionVoterCeremonyRecord CreateVoterCeremonyRecord(
        ElectionId electionId,
        string organizationVoterId,
        string linkedActorPublicAddress,
        int ballotDefinitionVersion,
        byte[] ballotDefinitionHash,
        string ceremonyProfileId = ElectionSp04ProfileIds.ChallengeSpoilV1,
        DateTime? createdAt = null,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        var timestamp = createdAt ?? DateTime.UtcNow;

        return new ElectionVoterCeremonyRecord(
            Guid.NewGuid(),
            electionId,
            NormalizeRequiredText(organizationVoterId, nameof(organizationVoterId)),
            NormalizeRequiredText(linkedActorPublicAddress, nameof(linkedActorPublicAddress)),
            NormalizeRequiredText(ceremonyProfileId, nameof(ceremonyProfileId)),
            ballotDefinitionVersion,
            CloneBytes(ballotDefinitionHash)!,
            PreparedPackageCount: 0,
            SpoiledPackageCount: 0,
            ElectionVoterCeremonyFinalState.None,
            timestamp,
            timestamp,
            FinalAcceptedBallotId: null,
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);
    }

    public static ElectionPreparedBallotCommitmentRecord CreatePreparedBallotCommitmentRecord(
        ElectionId electionId,
        string organizationVoterId,
        string linkedActorPublicAddress,
        string preparedBallotHash,
        int ballotDefinitionVersion,
        byte[] ballotDefinitionHash,
        string proofStatementId,
        DateTime? precommittedAt = null,
        TimeSpan? ttl = null,
        Guid? preparedBallotId = null,
        string ceremonyProfileId = ElectionSp04ProfileIds.ChallengeSpoilV1,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        var timestamp = precommittedAt ?? DateTime.UtcNow;
        var normalizedTtl = ttl ?? TimeSpan.FromMinutes(15);

        if (normalizedTtl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "Prepared ballot TTL must be positive.");
        }

        return new ElectionPreparedBallotCommitmentRecord(
            preparedBallotId ?? Guid.NewGuid(),
            electionId,
            NormalizeRequiredText(organizationVoterId, nameof(organizationVoterId)),
            NormalizeRequiredText(linkedActorPublicAddress, nameof(linkedActorPublicAddress)),
            NormalizeRequiredText(preparedBallotHash, nameof(preparedBallotHash)),
            ballotDefinitionVersion,
            CloneBytes(ballotDefinitionHash)!,
            NormalizeRequiredText(ceremonyProfileId, nameof(ceremonyProfileId)),
            NormalizeRequiredText(proofStatementId, nameof(proofStatementId)),
            ElectionPreparedBallotState.Prepared,
            timestamp,
            timestamp.Add(normalizedTtl),
            SpoilMarkerId: null,
            AcceptedBallotId: null,
            SpoiledAt: null,
            CastAt: null,
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);
    }

    public static ElectionSpoiledPreparedBallotRecord CreateSpoiledPreparedBallotRecord(
        ElectionId electionId,
        Guid preparedBallotId,
        string preparedBallotHash,
        string spoiledTranscriptHash,
        string spoilRecordHash,
        string localVerifierVersion,
        DateTime? spoiledAt = null,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null) =>
        new(
            Guid.NewGuid(),
            electionId,
            preparedBallotId,
            NormalizeRequiredText(preparedBallotHash, nameof(preparedBallotHash)),
            NormalizeRequiredText(spoiledTranscriptHash, nameof(spoiledTranscriptHash)),
            NormalizeRequiredText(spoilRecordHash, nameof(spoilRecordHash)),
            NormalizeRequiredText(localVerifierVersion, nameof(localVerifierVersion)),
            spoiledAt ?? DateTime.UtcNow,
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);

    public static ElectionBoundReceiptRecord CreateBoundReceiptRecord(
        ElectionId electionId,
        int ballotDefinitionVersion,
        byte[] ballotDefinitionHash,
        Guid preparedBallotId,
        string preparedBallotHash,
        string receiptSecret,
        string receiptCommitment,
        Guid acceptedBallotId,
        DateTime acceptedAt,
        string serverAcceptanceProof,
        string verifierProfileId,
        string ceremonyProfileId = ElectionSp04ProfileIds.ChallengeSpoilV1,
        string receiptVersion = "sp04-bound-receipt-v1",
        string receiptCommitmentScheme = "sha256(receipt_secret|prepared_ballot_hash|accepted_ballot_id)",
        string? clientApplicationId = null,
        string? clientApplicationVersion = null,
        string? clientApplicationReleaseHash = null) =>
        new(
            NormalizeRequiredText(receiptVersion, nameof(receiptVersion)),
            electionId,
            NormalizeRequiredText(ceremonyProfileId, nameof(ceremonyProfileId)),
            ballotDefinitionVersion,
            CloneBytes(ballotDefinitionHash)!,
            preparedBallotId,
            NormalizeRequiredText(preparedBallotHash, nameof(preparedBallotHash)),
            NormalizeRequiredText(receiptSecret, nameof(receiptSecret)),
            NormalizeRequiredText(receiptCommitment, nameof(receiptCommitment)),
            NormalizeRequiredText(receiptCommitmentScheme, nameof(receiptCommitmentScheme)),
            acceptedBallotId,
            acceptedAt,
            NormalizeRequiredText(serverAcceptanceProof, nameof(serverAcceptanceProof)),
            NormalizeRequiredText(verifierProfileId, nameof(verifierProfileId)),
            NormalizeOptionalText(clientApplicationId),
            NormalizeOptionalText(clientApplicationVersion),
            NormalizeOptionalText(clientApplicationReleaseHash));
}
