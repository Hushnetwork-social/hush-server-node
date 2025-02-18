using HushNode.Blockchain.Persistency.Abstractions;
using Microsoft.EntityFrameworkCore.Storage;

namespace HushNode.Blockchain.Persistency.EntityFramework;

public class UnitOfWork : IUnitOfWork
{
    private readonly BlockchainDbContext _dbContext;
    private readonly IDbContextTransaction _contextTransaction;

    public IBlockRepository BlockRepository { get; }

    public IBlockchainStateRepository BlockStateRepository { get; }

    public UnitOfWork(BlockchainDbContext dbContext)
    {
        this._dbContext = dbContext;
        this._contextTransaction = this._dbContext.Database.BeginTransaction();

        this.BlockRepository = new BlockRepository(_dbContext);
        this.BlockStateRepository = new BlockchainStateRepository(_dbContext);
    }

    public async Task CommitAsync()
    {
        await _dbContext.SaveChangesAsync();
        await _contextTransaction.CommitAsync();
    }

    public void Dispose()
    {
        this._contextTransaction.Dispose();
        this._dbContext.Dispose();
    }

    public async Task RollbackAsync()
    {
        await this._contextTransaction.RollbackAsync();
    }
}
