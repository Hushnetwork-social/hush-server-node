using HushShared.Blockchain.TransactionModel;

namespace HushNode.Indexing.Interfaces;

public interface IIndexStrategy
{
    bool CanHandle(AbstractTransaction transaction);

    Task HandleAsync(AbstractTransaction transaction);
}
