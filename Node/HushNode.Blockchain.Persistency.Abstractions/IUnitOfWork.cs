using HushNode.Blockchain.Persistency.Abstractions.Repositories;

namespace HushNode.Blockchain.Persistency.Abstractions;

public interface IUnitOfWork : IDisposable
{
    IBlockRepository BlockRepository { get; }

    IBlockchainStateRepository BlockStateRepository { get; }
    
    Task CommitAsync(); 
    
    Task RollbackAsync(); 
}
