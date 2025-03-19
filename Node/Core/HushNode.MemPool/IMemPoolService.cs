using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushNode.MemPool;

public interface IMemPoolService
{
    Task InitializeMemPoolAsync();

    Task AddVerifiedTransactionAsync<T>(ValidatedTransaction<T> validatedTransaction) 
        where T : ITransactionPayloadKind;

    Task<IReadOnlyList<AbstractTransaction>> GetPendingValidatedTransactionsAsync();
}
