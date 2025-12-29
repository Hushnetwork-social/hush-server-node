using HushShared.Blockchain.TransactionModel;

namespace HushShared.Feeds.Model;

/// <summary>
/// Payload for updating a Group Feed's title.
/// </summary>
public record UpdateGroupFeedTitlePayload(
    FeedId FeedId,
    string AdminPublicAddress,
    string NewTitle) : ITransactionPayloadKind;

public static class UpdateGroupFeedTitlePayloadHandler
{
    public static Guid UpdateGroupFeedTitlePayloadKind { get; } = Guid.Parse("d0e1f2a3-b4c5-3d4e-7f8a-9b0c1d2e3f4a");
}
