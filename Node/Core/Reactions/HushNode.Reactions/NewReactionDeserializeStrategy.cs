using System.Text.Json;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Reactions.Model;

namespace HushNode.Reactions;

/// <summary>
/// Deserializes reaction transaction payloads from JSON.
/// </summary>
public class NewReactionDeserializeStrategy : ITransactionDeserializerStrategy
{
    public bool CanDeserialize(string transactionKind) =>
        NewReactionPayloadHandler.NewReactionPayloadKind.ToString() == transactionKind;

    public AbstractTransaction DeserializeSignedTransaction(string transactionJSON) =>
        JsonSerializer.Deserialize<SignedTransaction<NewReactionPayload>>(transactionJSON)!;

    public AbstractTransaction DeserializeValidatedTransaction(string transactionJSON) =>
        JsonSerializer.Deserialize<ValidatedTransaction<NewReactionPayload>>(transactionJSON)!;
}
