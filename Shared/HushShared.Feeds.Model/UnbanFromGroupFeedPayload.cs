using HushShared.Blockchain.TransactionModel;

namespace HushShared.Feeds.Model;

/// <summary>
/// Payload for unbanning a previously banned member from a Group Feed.
/// TRIGGERS key rotation - reinstated member receives new key.
/// </summary>
public record UnbanFromGroupFeedPayload(
    FeedId FeedId,
    string AdminPublicAddress,
    string UnbannedUserPublicAddress) : ITransactionPayloadKind;

public static class UnbanFromGroupFeedPayloadHandler
{
    public static Guid UnbanFromGroupFeedPayloadKind { get; } = Guid.Parse("b8c9d0e1-f2a3-1b2c-5d6e-7f8a9b0c1d2e");
}
