namespace HushShared.Elections.Model;

public static class ElectionSp04ProfileIds
{
    public const string ChallengeSpoilV1 = "challenge_spoil_v1";
}

public record ElectionBallotDefinitionSealRecord(
    int BallotDefinitionVersion,
    byte[] BallotDefinitionHash,
    DateTime SealedAt,
    ElectionBallotDefinitionMutationPolicy MutationPolicy)
{
    public byte[] BallotDefinitionHash { get; init; } =
        BallotDefinitionHash is { Length: > 0 }
            ? BallotDefinitionHash.ToArray()
            : throw new ArgumentException("Ballot definition hash is required.", nameof(BallotDefinitionHash));
}

public record ElectionVoterCeremonyRecord(
    Guid Id,
    ElectionId ElectionId,
    string OrganizationVoterId,
    string LinkedActorPublicAddress,
    string CeremonyProfileId,
    int BallotDefinitionVersion,
    byte[] BallotDefinitionHash,
    int PreparedPackageCount,
    int SpoiledPackageCount,
    ElectionVoterCeremonyFinalState FinalState,
    DateTime CreatedAt,
    DateTime LastUpdatedAt,
    Guid? FinalAcceptedBallotId = null,
    Guid? SourceTransactionId = null,
    long? SourceBlockHeight = null,
    Guid? SourceBlockId = null)
{
    public string OrganizationVoterId { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            OrganizationVoterId,
            nameof(OrganizationVoterId));

    public string LinkedActorPublicAddress { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            LinkedActorPublicAddress,
            nameof(LinkedActorPublicAddress));

    public string CeremonyProfileId { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            CeremonyProfileId,
            nameof(CeremonyProfileId));

    public byte[] BallotDefinitionHash { get; init; } =
        BallotDefinitionHash is { Length: > 0 }
            ? BallotDefinitionHash.ToArray()
            : throw new ArgumentException("Ballot definition hash is required.", nameof(BallotDefinitionHash));
}

public record ElectionPreparedBallotCommitmentRecord(
    Guid PreparedBallotId,
    ElectionId ElectionId,
    string OrganizationVoterId,
    string LinkedActorPublicAddress,
    string PreparedBallotHash,
    int BallotDefinitionVersion,
    byte[] BallotDefinitionHash,
    string CeremonyProfileId,
    string ProofStatementId,
    ElectionPreparedBallotState State,
    DateTime PrecommittedAt,
    DateTime ExpiresAt,
    Guid? SpoilMarkerId = null,
    Guid? AcceptedBallotId = null,
    DateTime? SpoiledAt = null,
    DateTime? CastAt = null,
    Guid? SourceTransactionId = null,
    long? SourceBlockHeight = null,
    Guid? SourceBlockId = null)
{
    public string OrganizationVoterId { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            OrganizationVoterId,
            nameof(OrganizationVoterId));

    public string LinkedActorPublicAddress { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            LinkedActorPublicAddress,
            nameof(LinkedActorPublicAddress));

    public string PreparedBallotHash { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            PreparedBallotHash,
            nameof(PreparedBallotHash));

    public byte[] BallotDefinitionHash { get; init; } =
        BallotDefinitionHash is { Length: > 0 }
            ? BallotDefinitionHash.ToArray()
            : throw new ArgumentException("Ballot definition hash is required.", nameof(BallotDefinitionHash));

    public string CeremonyProfileId { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            CeremonyProfileId,
            nameof(CeremonyProfileId));

    public string ProofStatementId { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            ProofStatementId,
            nameof(ProofStatementId));

    public bool IsExpired(DateTime nowUtc) => nowUtc >= ExpiresAt;
}

public record ElectionSpoiledPreparedBallotRecord(
    Guid Id,
    ElectionId ElectionId,
    Guid PreparedBallotId,
    string PreparedBallotHash,
    string SpoiledTranscriptHash,
    string SpoilRecordHash,
    string LocalVerifierVersion,
    DateTime SpoiledAt,
    Guid? SourceTransactionId = null,
    long? SourceBlockHeight = null,
    Guid? SourceBlockId = null)
{
    public string PreparedBallotHash { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            PreparedBallotHash,
            nameof(PreparedBallotHash));

    public string SpoiledTranscriptHash { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            SpoiledTranscriptHash,
            nameof(SpoiledTranscriptHash));

    public string SpoilRecordHash { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            SpoilRecordHash,
            nameof(SpoilRecordHash));

    public string LocalVerifierVersion { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            LocalVerifierVersion,
            nameof(LocalVerifierVersion));
}

public record ElectionBoundReceiptRecord(
    string ReceiptVersion,
    ElectionId ElectionId,
    string CeremonyProfileId,
    int BallotDefinitionVersion,
    byte[] BallotDefinitionHash,
    Guid PreparedBallotId,
    string PreparedBallotHash,
    string ReceiptSecret,
    string ReceiptCommitment,
    string ReceiptCommitmentScheme,
    Guid AcceptedBallotId,
    DateTime AcceptedAt,
    string ServerAcceptanceProof,
    string VerifierProfileId,
    string? ClientApplicationId = null,
    string? ClientApplicationVersion = null,
    string? ClientApplicationReleaseHash = null)
{
    public string ReceiptVersion { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            ReceiptVersion,
            nameof(ReceiptVersion));

    public string CeremonyProfileId { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            CeremonyProfileId,
            nameof(CeremonyProfileId));

    public byte[] BallotDefinitionHash { get; init; } =
        BallotDefinitionHash is { Length: > 0 }
            ? BallotDefinitionHash.ToArray()
            : throw new ArgumentException("Ballot definition hash is required.", nameof(BallotDefinitionHash));

    public string PreparedBallotHash { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            PreparedBallotHash,
            nameof(PreparedBallotHash));

    public string ReceiptSecret { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            ReceiptSecret,
            nameof(ReceiptSecret));

    public string ReceiptCommitment { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            ReceiptCommitment,
            nameof(ReceiptCommitment));

    public string ReceiptCommitmentScheme { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            ReceiptCommitmentScheme,
            nameof(ReceiptCommitmentScheme));

    public string ServerAcceptanceProof { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            ServerAcceptanceProof,
            nameof(ServerAcceptanceProof));

    public string VerifierProfileId { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            VerifierProfileId,
            nameof(VerifierProfileId));
}
