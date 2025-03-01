using HushNode.Blockchain.Persistency.Abstractions.Models.Transaction;

namespace HushNode.Indexing;

public interface IIndexStrategy
{
    bool CanHandle(AbstractTransaction transaction);

    Task HandleAsync(AbstractTransaction transaction);
}
