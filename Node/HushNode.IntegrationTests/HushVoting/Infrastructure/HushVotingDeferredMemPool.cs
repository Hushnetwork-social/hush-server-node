using HushNode.MemPool;
using HushShared.Blockchain.TransactionModel;

namespace HushVoting.IntegrationTests.Infrastructure;

/// <summary>Delays inclusion without deleting or changing accepted transactions.</summary>
internal sealed class HushVotingDeferredMemPool(MemPoolService inner, HushVotingFaultInterceptor controls) : IMemPoolService
{
    public Task InitializeMemPoolAsync() => inner.InitializeMemPoolAsync();
    public void AddVerifiedTransaction(AbstractTransaction transaction) => inner.AddVerifiedTransaction(transaction);
    public IEnumerable<AbstractTransaction> PeekPendingValidatedTransactions() => inner.PeekPendingValidatedTransactions();
    public IEnumerable<AbstractTransaction> GetPendingValidatedTransactionsAsync()
    {
        if (controls.HoldMempoolDrain) return [];
        var transactions = inner.GetPendingValidatedTransactionsAsync().ToArray();
        var order = controls.NextBlockTransactionOrder;
        if (order is null) return transactions;
        controls.NextBlockTransactionOrder = null;
        // Select canonical block order among genuinely admitted transactions; never add,
        // alter, validate or manufacture one. Used to prove both same-block alternatives.
        if (transactions.Length != order.Count || transactions.Any(t => !order.Contains(t.TransactionId.Value)))
            throw new InvalidOperationException("Owned block ordering does not match admitted transactions.");
        return order.Select(id => transactions.Single(t => t.TransactionId.Value == id)).ToArray();
    }
}
