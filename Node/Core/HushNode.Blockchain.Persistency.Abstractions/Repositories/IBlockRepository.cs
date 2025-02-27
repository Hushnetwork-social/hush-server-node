using HushNode.Blockchain.Persistency.Abstractions.Models;

namespace HushNode.Blockchain.Persistency.Abstractions.Repositories;

public interface IBlockRepository : IRepository
{
    Task AddBlockchainBlockAsync(BlockchainBlock block);
}
