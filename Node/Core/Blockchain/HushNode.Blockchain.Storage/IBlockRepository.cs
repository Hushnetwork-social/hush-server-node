using Olimpo.EntityFramework.Persistency;
using HushNode.Blockchain.Storage.Model;

namespace HushNode.Blockchain.Storage;

public interface IBlockRepository : IRepository
{
    Task AddBlockchainBlockAsync(BlockchainBlock block);
}
