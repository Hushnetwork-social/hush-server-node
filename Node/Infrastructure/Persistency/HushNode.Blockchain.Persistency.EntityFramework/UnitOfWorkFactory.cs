using HushNode.Blockchain.Persistency.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HushNode.Blockchain.Persistency.EntityFramework
{
    public class UnitOfWorkFactory(
        IDbContextFactory<BlockchainDbContext> dbContextFactory,
        DbContextOptions<BlockchainDbContext> options) : IUnitOfWorkFactory
    {
        private readonly DbContextOptions<BlockchainDbContext> _options = options;

        private readonly IDbContextFactory<BlockchainDbContext> _dbContextFactory = dbContextFactory;


        public IUnitOfWork Create() => 
            new UnitOfWork(this._dbContextFactory);

        public IReadOnlyUnitOfWork CreateReadOnly() => 
            new ReadOnlyUnitOfWork(this._dbContextFactory);
    }
}
