using HushShared.Blockchain.TransactionModel;

namespace HushShared.Feeds.Model;

/// <summary>
/// Payload for creating a new Group Feed.
/// Contains group metadata and initial participants with their encrypted keys.
/// </summary>
public record NewGroupFeedPayload(
    FeedId FeedId,
    string Title,
    string Description,
    bool IsPublic,
    GroupFeedParticipant[] Participants) : ITransactionPayloadKind;

public static class NewGroupFeedPayloadHandler
{
    public static Guid NewGroupFeedPayloadKind { get; } = Guid.Parse("a1b2c3d4-e5f6-4a5b-8c9d-0e1f2a3b4c5d");
}
