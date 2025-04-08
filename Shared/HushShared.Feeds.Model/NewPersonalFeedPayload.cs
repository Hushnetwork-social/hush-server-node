using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushShared.Feeds.Model;

public record NewPersonalFeedPayload(
    FeedId FeedId,
    string Title,
    FeedType FeedType) : ITransactionPayloadKind;

public static class NewPersonalFeedPayloadHandler
{
    public static Guid NewPersonalFeedPayloadKind { get; } = Guid.Parse("70c718a9-14d0-4b70-ad37-fd8bfe184386");

    public static UnsignedTransaction<NewPersonalFeedPayload> CreateNewPersonalFeedTransaction() => 
        UnsignedTransactionHandler.CreateNew(
            NewPersonalFeedPayloadKind,
            Timestamp.Current,
            new NewPersonalFeedPayload(FeedId.NewFeedId, string.Empty, FeedType.Personal));
}