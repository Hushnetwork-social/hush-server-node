using HushNode.Blockchain.Persistency.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HushNode.Blockchain.Persistency.EntityFramework
{
    public class UnitOfWorkFactory(DbContextOptions<BlockchainDbContext> options) : IUnitOfWorkFactory
    {
        private readonly DbContextOptions<BlockchainDbContext> _options = options;

        public IUnitOfWork Create()
        {
            var context = new BlockchainDbContext(_options);
            return new UnitOfWork(context);
        }
    }
}
