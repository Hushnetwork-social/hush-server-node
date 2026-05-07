namespace HushNode.Elections;

public interface IElectionBallotPublicationCryptoService
{
    ElectionBallotPublicationPreparationResult PrepareForPublication(
        string encryptedBallotPackage,
        string proofBundle);

    ElectionBallotReplayResult ReplayPublishedBallots(
        IReadOnlyList<string> encryptedBallotPackages);
}

public sealed record ElectionBallotPublicationPreparationResult(
    bool IsSuccessful,
    string? PublishedEncryptedBallotPackage,
    string? PublishedProofBundle,
    string? PublicationWitnessMaterial,
    string? FailureCode,
    string? FailureReason)
{
    public static ElectionBallotPublicationPreparationResult Success(
        string publishedEncryptedBallotPackage,
        string publishedProofBundle,
        string? publicationWitnessMaterial = null) =>
        new(
            true,
            publishedEncryptedBallotPackage,
            publishedProofBundle,
            publicationWitnessMaterial,
            null,
            null);

    public static ElectionBallotPublicationPreparationResult Failure(
        string failureCode,
        string failureReason) =>
        new(
            false,
            null,
            null,
            null,
            failureCode,
            failureReason);
}

public sealed record ElectionBallotReplayResult(
    bool IsSuccessful,
    byte[]? FinalEncryptedTallyHash,
    string? FailureCode,
    string? FailureReason)
{
    public static ElectionBallotReplayResult Success(byte[] finalEncryptedTallyHash) =>
        new(
            true,
            finalEncryptedTallyHash,
            null,
            null);

    public static ElectionBallotReplayResult Failure(string failureCode, string failureReason) =>
        new(
            false,
            null,
            failureCode,
            failureReason);
}
