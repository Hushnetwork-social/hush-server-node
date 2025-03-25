namespace HushShared.Blockchain.TransactionModel;

public interface ITransactionDeserializerStrategy
{
    bool CanDeserialize(string transactionKind);

    AbstractTransaction DeserializeSignedTransaction(string transactionJSON);

    AbstractTransaction DeserializeValidatedTransaction(string transactionJSON);
}
