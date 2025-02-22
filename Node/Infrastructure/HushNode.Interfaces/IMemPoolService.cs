using HushNode.Blockchain.Persistency.Abstractions.Models.Transaction;
using HushNode.Blockchain.Persistency.Abstractions.Models.Transaction.States;

namespace HushNode.Interfaces;

public interface IMemPoolService
{
    Task AddVerifiedTransactionAsync<T>(ValidatedTransaction<T> validatedTransaction) 
        where T : ITransactionPayloadKind;

    Task<IReadOnlyList<ValidatedTransaction<T>>> GetPendingValidatedTransactionsAsync<T>()
       where T : ITransactionPayloadKind;
}
