using HushShared.Blockchain.TransactionModel;

namespace HushNode.MemPool;

public interface IMemPoolService
{
    Task InitializeMemPoolAsync();
    
    void AddVerifiedTransactionAsync(AbstractTransaction validatedTransaction);

    IEnumerable<AbstractTransaction> GetPendingValidatedTransactionsAsync();
}
