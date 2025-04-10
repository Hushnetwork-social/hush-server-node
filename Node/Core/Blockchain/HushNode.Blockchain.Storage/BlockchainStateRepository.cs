using Microsoft.EntityFrameworkCore;
using HushNode.Blockchain.Storage.Model;
using Olimpo.EntityFramework.Persistency;
using HushShared.Blockchain.BlockModel;
using HushNode.Caching;

namespace HushNode.Blockchain.Storage;

public class BlockchainStateRepository : RepositoryBase<BlockchainDbContext>, IBlockchainStateRepository
{
    public async Task<BlockchainState> GetCurrentStateAsync() => 
        await this.Context.BlockchainStates
            .SingleOrDefaultAsync() ?? new GenesisBlockchainState();

    public async Task InsertBlockchainStateAsync(IBlockchainCache blockchainCache)
    {
        var blockchainState = new BlockchainState(
            BlockchainStateId.NewBlockchainStateId,
            blockchainCache.LastBlockIndex,
            blockchainCache.CurrentBlockId,
            blockchainCache.NextBlockId,
            blockchainCache.NextBlockId);

        await Context.BlockchainStates.AddAsync(blockchainState);
    }

    public async Task UpdateBlockchainStateAsync(IBlockchainCache blockchainCache)
    {
        await this.Context.BlockchainStates
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.PreviousBlockId, blockchainCache.PreviousBlockId)
                .SetProperty(x => x.CurrentBlockId, blockchainCache.CurrentBlockId)
                .SetProperty(x => x.NextBlockId, blockchainCache.NextBlockId)
                .SetProperty(x => x.BlockIndex, blockchainCache.LastBlockIndex));
    }
}
