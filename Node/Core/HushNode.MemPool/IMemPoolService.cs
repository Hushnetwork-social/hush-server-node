using HushShared.Blockchain.TransactionModel;

namespace HushNode.MemPool;

public interface IMemPoolService
{
    Task InitializeMemPoolAsync();
    
    void AddVerifiedTransaction(AbstractTransaction validatedTransaction);

    IEnumerable<AbstractTransaction> GetPendingValidatedTransactionsAsync();
}
