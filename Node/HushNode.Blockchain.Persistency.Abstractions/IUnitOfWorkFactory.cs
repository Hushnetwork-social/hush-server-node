namespace HushNode.Blockchain.Persistency.Abstractions;

public interface IUnitOfWorkFactory
{
    IUnitOfWork Create();
}
