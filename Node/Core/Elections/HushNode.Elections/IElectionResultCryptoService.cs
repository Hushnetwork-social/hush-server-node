using HushShared.Elections.Model;

namespace HushNode.Elections;

public interface IElectionResultCryptoService
{
    ElectionAggregateReleaseResult TryReleaseAggregateTally(
        IReadOnlyList<string> encryptedBallotPackages,
        IReadOnlyList<ElectionFinalizationShareRecord> acceptedShares,
        int maxSupportedCount);

    string EncryptForElectionParticipants(
        string plaintextPayload,
        string nodeEncryptedElectionPrivateKey);
}

public sealed record ElectionAggregateReleaseResult(
    bool IsSuccessful,
    byte[]? FinalEncryptedTallyHash,
    IReadOnlyList<int>? DecodedCounts,
    string? FailureCode,
    string? FailureReason)
{
    public static ElectionAggregateReleaseResult Success(
        byte[] finalEncryptedTallyHash,
        IReadOnlyList<int> decodedCounts) =>
        new(
            true,
            finalEncryptedTallyHash,
            decodedCounts,
            null,
            null);

    public static ElectionAggregateReleaseResult Failure(
        string failureCode,
        string failureReason) =>
        new(
            false,
            null,
            null,
            failureCode,
            failureReason);
}
