using Microsoft.EntityFrameworkCore;
using Olimpo.EntityFramework.Persistency;
using HushNode.Blockchain.Model;
using HushNode.Blockchain.Persistency.Abstractions.Repositories;

namespace HushNode.Blockchain.Persistency.EntityFramework;

public class BlockchainStateRepository : RepositoryBase<BlockchainDbContext>, IBlockchainStateRepository
{
    public async Task<BlockchainState> GetCurrentStateAsync() => 
        await this.Context.BlockchainStates
            .SingleOrDefaultAsync() ?? new GenesisBlockchainState();

    public async Task SetBlockchainStateAsync(BlockchainState blockchainState)
    {
        var currentBlockchainStateExists = await this.Context.BlockchainStates.AnyAsync();

        if (currentBlockchainStateExists)
        {
            this.Context
                .Set<BlockchainState>()
                .Update(blockchainState);
        }
        else
        {
            await this.Context.BlockchainStates.AddAsync(blockchainState);
        }
    }
}
