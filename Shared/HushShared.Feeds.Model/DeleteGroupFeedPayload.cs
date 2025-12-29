using HushShared.Blockchain.TransactionModel;

namespace HushShared.Feeds.Model;

/// <summary>
/// Payload for deleting a Group Feed.
/// Only the owner can delete a group.
/// </summary>
public record DeleteGroupFeedPayload(
    FeedId FeedId,
    string AdminPublicAddress) : ITransactionPayloadKind;

public static class DeleteGroupFeedPayloadHandler
{
    public static Guid DeleteGroupFeedPayloadKind { get; } = Guid.Parse("f2a3b4c5-d6e7-5f6a-9b0c-1d2e3f4a5b6c");
}
