using HushShared.Blockchain.TransactionModel;

namespace HushShared.Feeds.Model;

/// <summary>
/// Payload for joining a Group Feed.
/// InvitationSignature is required for private groups.
/// </summary>
public record JoinGroupFeedPayload(
    FeedId FeedId,
    string JoiningUserPublicAddress,
    string? InvitationSignature = null) : ITransactionPayloadKind;

public static class JoinGroupFeedPayloadHandler
{
    public static Guid JoinGroupFeedPayloadKind { get; } = Guid.Parse("b2c3d4e5-f6a7-5b6c-9d0e-1f2a3b4c5d6e");
}
