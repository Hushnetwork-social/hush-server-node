using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;

namespace HushShared.Feeds.Model;

/// <summary>
/// Payload for sending a message in a Group Feed.
/// EncryptedContent is encrypted with the AES key for the specified KeyGeneration.
/// AuthorCommitment is for Protocol Omega anonymous reactions.
/// </summary>
public record NewGroupFeedMessagePayload(
    FeedMessageId MessageId,
    FeedId FeedId,
    string EncryptedContent,
    int KeyGeneration,
    FeedMessageId? ReplyToMessageId = null,
    byte[]? AuthorCommitment = null) : ITransactionPayloadKind;

public static class NewGroupFeedMessagePayloadHandler
{
    public static Guid NewGroupFeedMessagePayloadKind { get; } = Guid.Parse("b4c5d6e7-f8a9-7b8c-1d2e-3f4a5b6c7d8e");
}
