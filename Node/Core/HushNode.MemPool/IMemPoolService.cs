using HushNode.Blockchain.Persistency.Abstractions.Models.Transaction;
using HushNode.Blockchain.Persistency.Abstractions.Models.Transaction.States;

namespace HushNode.MemPool;

public interface IMemPoolService
{
    Task InitializeMemPoolAsync();

    Task AddVerifiedTransactionAsync<T>(ValidatedTransaction<T> validatedTransaction) 
        where T : ITransactionPayloadKind;

    Task<IReadOnlyList<AbstractTransaction>> GetPendingValidatedTransactionsAsync();
}
