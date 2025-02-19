using HushNode.Blockchain.Persistency.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;

namespace HushNode.Blockchain.Persistency.EntityFramework
{
    public class UnitOfWorkFactory(
        IDbContextFactory<BlockchainDbContext> dbContextFactory,
        DbContextOptions<BlockchainDbContext> options) : IUnitOfWorkFactory
    {
        private readonly DbContextOptions<BlockchainDbContext> _options = options;

        private readonly IDbContextFactory<BlockchainDbContext> _dbContextFactory = dbContextFactory;


        public IUnitOfWork Create()
        {
            // var context = new BlockchainDbContext(_options);

            return new UnitOfWork(this._dbContextFactory);
        }
    }
}
