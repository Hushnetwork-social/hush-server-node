using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushShared.Feeds.Model;

public record NewFeedMessagePayload(
    FeedMessageId FeedMessageId,
    FeedId FeedId,
    string MessageContent,
    FeedMessageId? ReplyToMessageId = null,
    int? KeyGeneration = null,
    byte[]? AuthorCommitment = null) : ITransactionPayloadKind;

public static class NewFeedMessagePayloadHandler
{
    public static Guid NewFeedMessagePayloadKind { get; } = Guid.Parse("3309d79b-92e9-4435-9b23-0de0b3d24264");

    public static UnsignedTransaction<NewFeedMessagePayload> CreateNewFeedMessageTransaction(
        FeedMessageId feedMessageId,
        FeedId feedId,
        string messageContent,
        FeedMessageId? replyToMessageId = null,
        int? keyGeneration = null,
        byte[]? authorCommitment = null) =>
        UnsignedTransactionHandler.CreateNew(
            NewFeedMessagePayloadKind,
            Timestamp.Current,
            new NewFeedMessagePayload(feedMessageId, feedId, messageContent, replyToMessageId, keyGeneration, authorCommitment));
}
