using HushShared.Blockchain.TransactionModel;

namespace HushShared.Feeds.Model;

/// <summary>
/// Payload for rotating the encryption key of a Group Feed.
/// Triggered by membership changes (join, leave, ban, unban) or manual admin action.
/// Contains encrypted keys for all current members.
/// </summary>
public record GroupFeedKeyRotationPayload(
    FeedId FeedId,
    int NewKeyGeneration,
    int PreviousKeyGeneration,
    long ValidFromBlock,
    GroupFeedEncryptedKey[] EncryptedKeys,
    RotationTrigger RotationTrigger) : ITransactionPayloadKind;

public static class GroupFeedKeyRotationPayloadHandler
{
    public static Guid GroupFeedKeyRotationPayloadKind { get; } = Guid.Parse("a3b4c5d6-e7f8-6a7b-0c1d-2e3f4a5b6c7d");
}
