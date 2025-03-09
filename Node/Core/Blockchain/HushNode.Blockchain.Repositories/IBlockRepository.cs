using Olimpo.EntityFramework.Persistency;
using HushNode.Blockchain.Model;

namespace HushNode.Blockchain.Repositories;

public interface IBlockRepository : IRepository
{
    Task AddBlockchainBlockAsync(BlockchainBlock block);
}
