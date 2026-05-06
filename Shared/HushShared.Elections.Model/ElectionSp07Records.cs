namespace HushShared.Elections.Model;

public static class ElectionSp07ProfileIds
{
    public const string PublicationProofMode = "zk_rerandomization_shuffle_v1";
    public const string ProofConstruction = "bayer_groth_reencryption_shuffle_argument_v1";
    public const string StatementId = "sp07-bayer-groth-hush-vector-shuffle-v1";
    public const string TranscriptVersion = "PublicationProofTranscript-v1";
    public const string ProofSystemVersion = "sp07-bayer-groth-hush-vector-shuffle-v1.0.0";
    public const string ExternalReviewStatus = "external_crypto_review_pending";

    public const int HighAssuranceV1MaxAcceptedBallots = 500;
    public const int HighAssuranceV1MaxEncryptedSlots = 8;
    public const int HighAssuranceV1MaxPublicationChunks = 5;
}

public enum ElectionPublicationWitnessCustodyStatus
{
    Sealed = 0,
    Deleted = 1,
    DeletionFailed = 2,
}

public enum ElectionPublicationProofSessionStatus
{
    Pending = 0,
    Generating = 1,
    SelfVerifying = 2,
    Verified = 3,
    Failed = 4,
    WitnessDeleted = 5,
}

public enum ElectionPublicationWitnessDeletionStatus
{
    NotStarted = 0,
    Completed = 1,
    Failed = 2,
}

public record ElectionPublicationWitnessRecord(
    Guid Id,
    ElectionId ElectionId,
    Guid WitnessSetId,
    Guid AcceptedBallotId,
    long? PublishedSequence,
    string AcceptedEncryptedBallotHash,
    string? PublishedEncryptedBallotHash,
    string ProofMode,
    string ProofConstruction,
    string StatementId,
    string ProofProfileVersion,
    string SealedWitnessMaterial,
    string SealedWitnessMaterialHash,
    string SealAlgorithm,
    ElectionPublicationWitnessCustodyStatus CustodyStatus,
    DateTime CreatedAt,
    DateTime? DeletedAt = null)
{
    public string AcceptedEncryptedBallotHash { get; init; } =
        NormalizeRequiredValue(AcceptedEncryptedBallotHash, nameof(AcceptedEncryptedBallotHash));

    public string? PublishedEncryptedBallotHash { get; init; } =
        NormalizeOptionalValue(PublishedEncryptedBallotHash);

    public string ProofMode { get; init; } = NormalizeRequiredValue(ProofMode, nameof(ProofMode));

    public string ProofConstruction { get; init; } =
        NormalizeRequiredValue(ProofConstruction, nameof(ProofConstruction));

    public string StatementId { get; init; } = NormalizeRequiredValue(StatementId, nameof(StatementId));

    public string ProofProfileVersion { get; init; } =
        NormalizeRequiredValue(ProofProfileVersion, nameof(ProofProfileVersion));

    public string SealedWitnessMaterial { get; init; } =
        NormalizeRequiredValue(SealedWitnessMaterial, nameof(SealedWitnessMaterial));

    public string SealedWitnessMaterialHash { get; init; } =
        NormalizeRequiredValue(SealedWitnessMaterialHash, nameof(SealedWitnessMaterialHash));

    public string SealAlgorithm { get; init; } = NormalizeRequiredValue(SealAlgorithm, nameof(SealAlgorithm));

    internal static string NormalizeRequiredValue(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", paramName);
        }

        return value.Trim();
    }

    internal static string? NormalizeOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public record ElectionPublicationProofSessionRecord(
    Guid Id,
    ElectionId ElectionId,
    Guid WitnessSetId,
    string ProofMode,
    string ProofConstruction,
    string StatementId,
    ElectionPublicationProofSessionStatus Status,
    DateTime StartedAt,
    DateTime? CompletedAt,
    int AcceptedBallotCount,
    int PublishedBallotCount,
    int ChunkCount,
    int RetryCount,
    string? FailureCode,
    string? FailureReason,
    string? AcceptedBallotSetHash,
    string? PublishedBallotStreamHash,
    string? TranscriptHash,
    string? ProofHash,
    string? ServerVerifierOutputHash,
    Guid? DeletionReceiptId)
{
    public string ProofMode { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeRequiredValue(ProofMode, nameof(ProofMode));

    public string ProofConstruction { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeRequiredValue(ProofConstruction, nameof(ProofConstruction));

    public string StatementId { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeRequiredValue(StatementId, nameof(StatementId));

    public string? FailureCode { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeOptionalValue(FailureCode);

    public string? FailureReason { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeOptionalValue(FailureReason);

    public string? AcceptedBallotSetHash { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeOptionalValue(AcceptedBallotSetHash);

    public string? PublishedBallotStreamHash { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeOptionalValue(PublishedBallotStreamHash);

    public string? TranscriptHash { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeOptionalValue(TranscriptHash);

    public string? ProofHash { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeOptionalValue(ProofHash);

    public string? ServerVerifierOutputHash { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeOptionalValue(ServerVerifierOutputHash);
}

public record ElectionPublicationProofTranscriptRecord(
    Guid Id,
    ElectionId ElectionId,
    Guid ProofSessionId,
    Guid WitnessSetId,
    string TranscriptVersion,
    string ProofMode,
    string ProofConstruction,
    string StatementId,
    string ProfileId,
    string BallotDefinitionHash,
    string BallotEncryptionSchemeVersion,
    string ElectionPublicKeyId,
    string AcceptedBallotSetHash,
    string PublishedBallotStreamHash,
    int AcceptedBallotCount,
    int PublishedBallotCount,
    int CiphertextSlotCount,
    string ProofSystemVersion,
    string ProofBytes,
    string ProofHash,
    string TranscriptHash,
    string ExternalReviewStatus,
    DateTime GeneratedAt,
    string? GeneratorReleaseHash,
    string? VerifierReleaseHash,
    IReadOnlyList<string> PublicPrivacyBoundary)
{
    public string TranscriptVersion { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeRequiredValue(TranscriptVersion, nameof(TranscriptVersion));

    public string ProofMode { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeRequiredValue(ProofMode, nameof(ProofMode));

    public string ProofConstruction { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeRequiredValue(ProofConstruction, nameof(ProofConstruction));

    public string StatementId { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeRequiredValue(StatementId, nameof(StatementId));

    public string ProfileId { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeRequiredValue(ProfileId, nameof(ProfileId));

    public string BallotDefinitionHash { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeRequiredValue(BallotDefinitionHash, nameof(BallotDefinitionHash));

    public string BallotEncryptionSchemeVersion { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeRequiredValue(
            BallotEncryptionSchemeVersion,
            nameof(BallotEncryptionSchemeVersion));

    public string ElectionPublicKeyId { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeRequiredValue(ElectionPublicKeyId, nameof(ElectionPublicKeyId));

    public string AcceptedBallotSetHash { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeRequiredValue(AcceptedBallotSetHash, nameof(AcceptedBallotSetHash));

    public string PublishedBallotStreamHash { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeRequiredValue(
            PublishedBallotStreamHash,
            nameof(PublishedBallotStreamHash));

    public string ProofSystemVersion { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeRequiredValue(ProofSystemVersion, nameof(ProofSystemVersion));

    public string ProofBytes { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeRequiredValue(ProofBytes, nameof(ProofBytes));

    public string ProofHash { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeRequiredValue(ProofHash, nameof(ProofHash));

    public string TranscriptHash { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeRequiredValue(TranscriptHash, nameof(TranscriptHash));

    public string ExternalReviewStatus { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeRequiredValue(ExternalReviewStatus, nameof(ExternalReviewStatus));

    public string? GeneratorReleaseHash { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeOptionalValue(GeneratorReleaseHash);

    public string? VerifierReleaseHash { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeOptionalValue(VerifierReleaseHash);

    public IReadOnlyList<string> PublicPrivacyBoundary { get; init; } =
        PublicPrivacyBoundary is null
            ? Array.Empty<string>()
            : PublicPrivacyBoundary
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
}

public record ElectionPublicationWitnessDeletionReceiptRecord(
    Guid Id,
    ElectionId ElectionId,
    Guid ProofSessionId,
    Guid WitnessSetId,
    string WitnessSetHash,
    int WitnessCount,
    string TranscriptHash,
    string ProofHash,
    ElectionPublicationWitnessDeletionStatus DeletionStatus,
    DateTime DeletedAt,
    string? DeletionActorRef,
    string? FailureCode,
    string? FailureReason)
{
    public string WitnessSetHash { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeRequiredValue(WitnessSetHash, nameof(WitnessSetHash));

    public string TranscriptHash { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeRequiredValue(TranscriptHash, nameof(TranscriptHash));

    public string ProofHash { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeRequiredValue(ProofHash, nameof(ProofHash));

    public string? DeletionActorRef { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeOptionalValue(DeletionActorRef);

    public string? FailureCode { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeOptionalValue(FailureCode);

    public string? FailureReason { get; init; } =
        ElectionPublicationWitnessRecord.NormalizeOptionalValue(FailureReason);
}
