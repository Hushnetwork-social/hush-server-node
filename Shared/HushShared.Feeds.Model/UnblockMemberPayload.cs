using HushShared.Blockchain.TransactionModel;

namespace HushShared.Feeds.Model;

/// <summary>
/// Payload for unblocking a previously blocked member in a Group Feed.
/// </summary>
public record UnblockMemberPayload(
    FeedId FeedId,
    string AdminPublicAddress,
    string UnblockedUserPublicAddress) : ITransactionPayloadKind;

public static class UnblockMemberPayloadHandler
{
    public static Guid UnblockMemberPayloadKind { get; } = Guid.Parse("f6a7b8c9-d0e1-9f0a-3b4c-5d6e7f8a9b0c");
}
