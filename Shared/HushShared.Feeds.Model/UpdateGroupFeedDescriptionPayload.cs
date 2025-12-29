using HushShared.Blockchain.TransactionModel;

namespace HushShared.Feeds.Model;

/// <summary>
/// Payload for updating a Group Feed's description.
/// </summary>
public record UpdateGroupFeedDescriptionPayload(
    FeedId FeedId,
    string AdminPublicAddress,
    string NewDescription) : ITransactionPayloadKind;

public static class UpdateGroupFeedDescriptionPayloadHandler
{
    public static Guid UpdateGroupFeedDescriptionPayloadKind { get; } = Guid.Parse("e1f2a3b4-c5d6-4e5f-8a9b-0c1d2e3f4a5b");
}
