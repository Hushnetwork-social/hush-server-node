namespace HushShared.Blockchain.TransactionModel;

public interface ITransactionContentHandler
{
    bool CanValidate(Guid transactionKind);

    AbstractTransaction? ValidateAndSign(AbstractTransaction transaction);
}
