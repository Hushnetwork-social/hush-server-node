using Microsoft.EntityFrameworkCore;
using Olimpo.EntityFramework.Persistency;
using HushNode.Blockchain.Storage.Model;

namespace HushNode.Blockchain.Storage;

public class BlockchainStateRepository : RepositoryBase<BlockchainDbContext>, IBlockchainStateRepository
{
    public async Task<BlockchainState> GetCurrentStateAsync() => 
        await this.Context.BlockchainStates
            .SingleOrDefaultAsync() ?? new GenesisBlockchainState();

    public async Task SetBlockchainStateAsync(BlockchainState blockchainState)
    {
        var currentBlockchainStateExists = await Context.BlockchainStates.AnyAsync();

        if (currentBlockchainStateExists)
        {
            Context
                .Set<BlockchainState>()
                .Update(blockchainState);
        }
        else
        {
            await Context.BlockchainStates.AddAsync(blockchainState);
        }
    }
}
