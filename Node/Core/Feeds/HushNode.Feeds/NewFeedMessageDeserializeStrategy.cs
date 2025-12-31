using System.Text.Json;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class NewFeedMessageDeserializeStrategy : ITransactionDeserializerStrategy
{
    public bool CanDeserialize(string transactionKind) => 
        NewFeedMessagePayloadHandler.NewFeedMessagePayloadKind.ToString() == transactionKind;

    public AbstractTransaction DeserializeSignedTransaction(string transactionJSON) =>
        JsonSerializer.Deserialize<SignedTransaction<NewFeedMessagePayload>>(transactionJSON)!;

    public AbstractTransaction DeserializeValidatedTransaction(string transactionJSON) =>
        JsonSerializer.Deserialize<ValidatedTransaction<NewFeedMessagePayload>>(transactionJSON)!;
}
