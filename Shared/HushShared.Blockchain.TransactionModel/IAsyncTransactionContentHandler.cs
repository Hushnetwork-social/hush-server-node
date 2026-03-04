namespace HushShared.Blockchain.TransactionModel;

public interface IAsyncTransactionContentHandler
{
    Task<AbstractTransaction?> ValidateAndSignAsync(AbstractTransaction transaction);
}
