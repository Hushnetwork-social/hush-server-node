using HushNode.MemPool;
using HushShared.Blockchain.TransactionModel;

namespace HushVoting.IntegrationTests.Infrastructure;

/// <summary>Delays inclusion without deleting or changing accepted transactions.</summary>
internal sealed class HushVotingDeferredMemPool(MemPoolService inner, HushVotingFaultInterceptor controls) : IMemPoolService
{
    public Task InitializeMemPoolAsync() => inner.InitializeMemPoolAsync();
    public void AddVerifiedTransaction(AbstractTransaction transaction) => inner.AddVerifiedTransaction(transaction);
    public IEnumerable<AbstractTransaction> PeekPendingValidatedTransactions() => inner.PeekPendingValidatedTransactions();
    public IEnumerable<AbstractTransaction> GetPendingValidatedTransactionsAsync() =>
        controls.HoldMempoolDrain ? [] : inner.GetPendingValidatedTransactionsAsync();
}
