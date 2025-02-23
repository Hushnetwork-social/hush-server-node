using HushNode.Blockchain.Persistency.Abstractions;
using HushNode.Blockchain.Persistency.Abstractions.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HushNode.Blockchain.Persistency.EntityFramework;

public class UnitOfWork : IUnitOfWork
{
    private BlockchainDbContext _dbContext;
    private IDbContextTransaction _contextTransaction;
    private readonly IDbContextFactory<BlockchainDbContext> _dbFactoryContext;
    private bool _disposed = false;

    public IBlockRepository BlockRepository { get; private set; }

    public IBlockchainStateRepository BlockStateRepository { get; private set; }

    public UnitOfWork(IDbContextFactory<BlockchainDbContext> dbFactoryContext)
    {
        this._dbFactoryContext = dbFactoryContext;

        this._dbContext = this._dbFactoryContext.CreateDbContext();
        this._contextTransaction = this._dbContext.Database.BeginTransaction();

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

    public async Task CommitAsync()
    {
        try
        {
            await _dbContext.SaveChangesAsync();
            await _contextTransaction.CommitAsync();

            this._contextTransaction?.Dispose();
            this._dbContext.Dispose();
            this._dbContext = this._dbFactoryContext.CreateDbContext();
            this._contextTransaction = this._dbContext.Database.BeginTransaction();

            this.BlockRepository = new BlockRepository(_dbContext);
            this.BlockStateRepository = new BlockchainStateRepository(_dbContext);
        }
        catch
        {
            await this.RollbackAsync();
            throw;
        }
    }

    public async Task RollbackAsync()
    {
        await this._contextTransaction.RollbackAsync();
    }

    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~UnitOfWork()
    {
        this.Dispose(false);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!this._disposed)
        {
            if (disposing)
            {
                this._contextTransaction?.Dispose();
                this._dbContext?.Dispose();
            }

            this._disposed = true;
        }
    }
}
