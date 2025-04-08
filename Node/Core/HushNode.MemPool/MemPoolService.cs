using System.Collections.Concurrent;
using HushShared.Blockchain.TransactionModel;

namespace HushNode.MemPool;

public class MemPoolService : IMemPoolService
{
    private ConcurrentBag<AbstractTransaction> _nextBlockTransactionsCandidate;

    public MemPoolService()
    {
        this._nextBlockTransactionsCandidate = [];
    }

    public IEnumerable<AbstractTransaction> GetPendingValidatedTransactionsAsync() => 
        this._nextBlockTransactionsCandidate.TakeAndRemove(1000);

    public void AddVerifiedTransaction(AbstractTransaction validatedTransaction) => 
        this._nextBlockTransactionsCandidate.Add(validatedTransaction);
    

    public Task InitializeMemPoolAsync()
    {
        // TODO [AboimPinto]: In case of beeing part of an established network, 
        //                    the mempool should be initialized with the pending transactions from the other nodes.
        return Task.CompletedTask;
    }
}
