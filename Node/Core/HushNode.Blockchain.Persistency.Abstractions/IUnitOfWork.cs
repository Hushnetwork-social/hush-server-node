using HushNode.Blockchain.Persistency.Abstractions.Repositories;

namespace HushNode.Blockchain.Persistency.Abstractions;

public interface IReadOnlyUnitOfWork : IDisposable
{
    IBlockRepository BlockRepository { get; }

    IBlockchainStateRepository BlockStateRepository { get; }

    void DetachEntity<TEntity>(TEntity entity) 
        where TEntity : class;

    bool IsEntityTracked<TEntity>(TEntity entity) 
        where TEntity : class;
}

public interface IUnitOfWork : IReadOnlyUnitOfWork
{
    Task CommitAsync(); 
    
    Task RollbackAsync(); 
}
