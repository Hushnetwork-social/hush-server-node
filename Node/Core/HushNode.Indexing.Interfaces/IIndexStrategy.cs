using HushNode.Blockchain.Model.Transaction;

namespace HushNode.Indexing.Interfaces;

public interface IIndexStrategy
{
    bool CanHandle(AbstractTransaction transaction);

    Task HandleAsync(AbstractTransaction transaction);
}
