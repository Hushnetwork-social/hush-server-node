using HushNode.Blockchain.Persistency.Abstractions;
using HushNode.Blockchain.Persistency.Abstractions.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HushNode.Blockchain.Persistency.EntityFramework;

public class UnitOfWork : IUnitOfWork
{
    private readonly BlockchainDbContext _dbContext;
    private readonly IDbContextTransaction _contextTransaction;
    private bool _disposed = false;

    public IBlockRepository BlockRepository { get; }

    public IBlockchainStateRepository BlockStateRepository { get; }

    public UnitOfWork(IDbContextFactory<BlockchainDbContext> dbFactoryContext)
    {
        this._dbContext = dbFactoryContext.CreateDbContext();
        this._contextTransaction = this._dbContext.Database.BeginTransaction();

        this.BlockRepository = new BlockRepository(_dbContext);
        this.BlockStateRepository = new BlockchainStateRepository(_dbContext);
    }

    public async Task CommitAsync()
    {
        try
        {
            await _dbContext.SaveChangesAsync();
            await _contextTransaction.CommitAsync();
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
