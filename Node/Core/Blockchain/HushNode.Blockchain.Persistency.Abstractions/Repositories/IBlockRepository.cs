using HushNode.Blockchain.Persistency.Abstractions.Models;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Blockchain.Persistency.Abstractions.Repositories;

public interface IBlockRepository : IRepository
{
    Task AddBlockchainBlockAsync(BlockchainBlock block);
}
