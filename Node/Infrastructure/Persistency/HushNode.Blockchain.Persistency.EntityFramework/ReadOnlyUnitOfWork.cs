using HushNode.Blockchain.Persistency.Abstractions;
using HushNode.Blockchain.Persistency.Abstractions.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HushNode.Blockchain.Persistency.EntityFramework;

public class ReadOnlyUnitOfWork : IReadOnlyUnitOfWork
{
    private readonly BlockchainDbContext _dbContext;

    public IBlockRepository BlockRepository { get; }

    public IBlockchainStateRepository BlockStateRepository { get; }

    public ReadOnlyUnitOfWork(IDbContextFactory<BlockchainDbContext> dbFactoryContext)
    {
        this._dbContext = dbFactoryContext.CreateDbContext();
        this._dbContext.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        this.BlockRepository = new BlockRepository(_dbContext);
        this.BlockStateRepository = new BlockchainStateRepository(_dbContext);
    }

    public void DetachEntity<TEntity>(TEntity entity) 
        where TEntity : class
    {
        this._dbContext.Entry(entity).State = EntityState.Detached;
    }

    public bool IsEntityTracked<TEntity>(TEntity entity) where TEntity : class
    {
        var entry = this._dbContext.ChangeTracker.Entries<TEntity>()
            .FirstOrDefault(e => e.Entity == entity);

        return entry != null;
    }

    public void Dispose() => this._dbContext.Dispose();
}
