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
        _dbContext = dbContext;
        _contextTransaction = _dbContext.Database.BeginTransaction();

        BlockRepository = new BlockRepository(_dbContext);
        BlockStateRepository = new BlockchainStateRepository(_dbContext);
    }

    public async Task CommitAsync()
    {
        await _dbContext.SaveChangesAsync();
        await _contextTransaction.CommitAsync();
    }

    public void Dispose()
    {
        _contextTransaction.Dispose();
        _dbContext.Dispose();
    }

    public async Task RollbackAsync()
    {
        await _contextTransaction.RollbackAsync();
    }
}
