namespace HushNode.Blockchain.Persistency.Abstractions;

public interface IUnitOfWorkFactory
{
    IReadOnlyUnitOfWork CreateReadOnly();
    
    IUnitOfWork Create();
}
