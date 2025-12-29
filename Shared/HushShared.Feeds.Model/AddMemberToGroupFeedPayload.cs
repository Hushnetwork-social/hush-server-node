using HushShared.Blockchain.TransactionModel;

namespace HushShared.Feeds.Model;

/// <summary>
/// Payload for an admin adding a member to a Group Feed.
/// NewMemberPublicEncryptKey is used to encrypt the feed key for the new member.
/// </summary>
public record AddMemberToGroupFeedPayload(
    FeedId FeedId,
    string AdminPublicAddress,
    string NewMemberPublicAddress,
    string NewMemberPublicEncryptKey) : ITransactionPayloadKind;

public static class AddMemberToGroupFeedPayloadHandler
{
    public static Guid AddMemberToGroupFeedPayloadKind { get; } = Guid.Parse("d4e5f6a7-b8c9-7d8e-1f2a-3b4c5d6e7f8a");
}
