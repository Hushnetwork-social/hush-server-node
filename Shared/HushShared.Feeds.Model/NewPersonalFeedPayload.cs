using HushShared.Blockchain.TransactionModel;

namespace HushShared.Feeds.Model;

public record NewPersonalFeedPayload(
    FeedId FeedId,
    string Title,
    FeedType FeedType) : ITransactionPayloadKind;
