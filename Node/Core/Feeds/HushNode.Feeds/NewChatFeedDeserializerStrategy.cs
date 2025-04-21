using System.Text.Json;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class NewChatFeedDeserializerStrategy : ITransactionDeserializerStrategy
{
    public bool CanDeserialize(string transactionKind) => 
        NewChatFeedPayloadHandler.NewChatFeedPayloadKind.ToString() == transactionKind;

    public AbstractTransaction DeserializeSignedTransaction(string transactionJSON) => 
        JsonSerializer.Deserialize<SignedTransaction<NewChatFeedPayload>>(transactionJSON);

    public AbstractTransaction DeserializeValidatedTransaction(string transactionJSON) => 
        JsonSerializer.Deserialize<ValidatedTransaction<NewChatFeedPayload>>(transactionJSON);
}
