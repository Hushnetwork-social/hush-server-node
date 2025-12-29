using HushShared.Blockchain.TransactionModel;

namespace HushShared.Feeds.Model;

/// <summary>
/// Payload for banning a member from a Group Feed.
/// TRIGGERS key rotation - banned member loses access to future messages.
/// </summary>
public record BanFromGroupFeedPayload(
    FeedId FeedId,
    string AdminPublicAddress,
    string BannedUserPublicAddress,
    string? Reason = null) : ITransactionPayloadKind;

public static class BanFromGroupFeedPayloadHandler
{
    public static Guid BanFromGroupFeedPayloadKind { get; } = Guid.Parse("a7b8c9d0-e1f2-0a1b-4c5d-6e7f8a9b0c1d");
}
