using HushShared.Blockchain.TransactionModel;

namespace HushShared.Feeds.Model;

/// <summary>
/// Payload for voluntarily leaving a Group Feed.
/// Triggers key rotation for remaining members.
/// </summary>
public record LeaveGroupFeedPayload(
    FeedId FeedId,
    string LeavingUserPublicAddress) : ITransactionPayloadKind;

public static class LeaveGroupFeedPayloadHandler
{
    public static Guid LeaveGroupFeedPayloadKind { get; } = Guid.Parse("c3d4e5f6-a7b8-6c7d-0e1f-2a3b4c5d6e7f");
}
