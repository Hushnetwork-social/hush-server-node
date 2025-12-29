using HushShared.Blockchain.TransactionModel;

namespace HushShared.Feeds.Model;

/// <summary>
/// Payload for blocking a member from posting in a Group Feed.
/// Does NOT trigger key rotation - blocked member can still decrypt messages.
/// </summary>
public record BlockMemberPayload(
    FeedId FeedId,
    string AdminPublicAddress,
    string BlockedUserPublicAddress,
    string? Reason = null) : ITransactionPayloadKind;

public static class BlockMemberPayloadHandler
{
    public static Guid BlockMemberPayloadKind { get; } = Guid.Parse("e5f6a7b8-c9d0-8e9f-2a3b-4c5d6e7f8a9b");
}
