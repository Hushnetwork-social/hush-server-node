using HushShared.Blockchain.TransactionModel;
using HushShared.Feeds.Model;

namespace HushShared.Bank.Model;

public record SendFundsPayload(
    FundsTransactionId FundsTransactionId,
    FeedId FeedId,
    string ToAddress,
    string Token,
    string Amount) : ITransactionPayloadKind;
