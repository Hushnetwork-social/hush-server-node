using HushShared.Elections.Model;

namespace HushShared.Elections.Verification.Model;

public record ElectionSp04PolicyRecord(
    string CeremonyProfileId,
    int RequiredChallengeCount,
    int PreparedPackageTtlSeconds,
    ElectionBallotDefinitionMutationPolicy BallotDefinitionMutationPolicy)
{
    public string CeremonyProfileId { get; init; } =
        string.IsNullOrWhiteSpace(CeremonyProfileId)
            ? throw new ArgumentException("Ceremony profile id is required.", nameof(CeremonyProfileId))
            : CeremonyProfileId.Trim();
}

public record ElectionSp04EvidenceRecord(
    ElectionId ElectionId,
    ElectionSp04PolicyRecord Policy,
    int BallotDefinitionVersion,
    byte[] BallotDefinitionHash,
    DateTime BallotDefinitionSealedAt,
    int PreparedPackageCount,
    int SpoiledPackageCount,
    int AcceptedBoundReceiptCount,
    string ReceiptCommitmentSetHash,
    IReadOnlyList<string> PublicPrivacyBoundary)
{
    public byte[] BallotDefinitionHash { get; init; } =
        BallotDefinitionHash is { Length: > 0 }
            ? BallotDefinitionHash.ToArray()
            : throw new ArgumentException("Ballot definition hash is required.", nameof(BallotDefinitionHash));

    public string ReceiptCommitmentSetHash { get; init; } =
        string.IsNullOrWhiteSpace(ReceiptCommitmentSetHash)
            ? throw new ArgumentException("Receipt commitment set hash is required.", nameof(ReceiptCommitmentSetHash))
            : ReceiptCommitmentSetHash.Trim();

    public IReadOnlyList<string> PublicPrivacyBoundary { get; init; } =
        PublicPrivacyBoundary
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

public record ElectionSp04ReceiptCommitmentRecord(
    Guid AcceptedBallotId,
    Guid PreparedBallotId,
    string PreparedBallotHash,
    string ReceiptCommitment,
    string ReceiptCommitmentScheme,
    DateTime AcceptedAt)
{
    public string PreparedBallotHash { get; init; } =
        string.IsNullOrWhiteSpace(PreparedBallotHash)
            ? throw new ArgumentException("Prepared ballot hash is required.", nameof(PreparedBallotHash))
            : PreparedBallotHash.Trim();

    public string ReceiptCommitment { get; init; } =
        string.IsNullOrWhiteSpace(ReceiptCommitment)
            ? throw new ArgumentException("Receipt commitment is required.", nameof(ReceiptCommitment))
            : ReceiptCommitment.Trim();

    public string ReceiptCommitmentScheme { get; init; } =
        string.IsNullOrWhiteSpace(ReceiptCommitmentScheme)
            ? throw new ArgumentException("Receipt commitment scheme is required.", nameof(ReceiptCommitmentScheme))
            : ReceiptCommitmentScheme.Trim();
}

public record ElectionSp04RestrictedCeremonyRecord(
    Guid CeremonyId,
    ElectionId ElectionId,
    string OrganizationVoterId,
    string LinkedActorPublicAddress,
    string CeremonyProfileId,
    int PreparedPackageCount,
    int SpoiledPackageCount,
    ElectionVoterCeremonyFinalState FinalState,
    Guid? FinalAcceptedBallotId);

public record ElectionSp04RestrictedPreparedBallotRecord(
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
    Guid? SpoilMarkerId,
    Guid? AcceptedBallotId)
{
    public byte[] BallotDefinitionHash { get; init; } =
        BallotDefinitionHash is { Length: > 0 }
            ? BallotDefinitionHash.ToArray()
            : throw new ArgumentException("Ballot definition hash is required.", nameof(BallotDefinitionHash));
}

public record ElectionSp04RestrictedSpoilMarkerRecord(
    Guid SpoilMarkerId,
    ElectionId ElectionId,
    Guid PreparedBallotId,
    string PreparedBallotHash,
    string SpoiledTranscriptHash,
    string SpoilRecordHash,
    string LocalVerifierVersion,
    DateTime SpoiledAt);
