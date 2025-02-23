using HushNode.Blockchain.Persistency.Abstractions.Models.Transaction;
using HushNode.Blockchain.Persistency.Abstractions.Models.Transaction.States;

namespace HushNode.MemPool;

public class MemPoolService : IMemPoolService
{
    public Task AddVerifiedTransactionAsync<T>(ValidatedTransaction<T> validatedTransaction) where T : ITransactionPayloadKind
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AbstractTransaction>> GetPendingValidatedTransactionsAsync() => 
            Task.FromResult((IReadOnlyList<AbstractTransaction>)new List<AbstractTransaction>().AsReadOnly());

    public Task InitializeMemPoolAsync()
    {
        // TODO [AboimPinto]: In case of beeing part of an established network, 
        //                    the mempool should be initialized with the pendiging transactions from the other nodes.
        return Task.CompletedTask;
    }
}
