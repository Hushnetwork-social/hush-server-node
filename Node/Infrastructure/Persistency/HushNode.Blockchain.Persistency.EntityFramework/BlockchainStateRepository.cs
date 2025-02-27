using HushNode.Blockchain.Persistency.Abstractions.Models;
using HushNode.Blockchain.Persistency.Abstractions.Repositories;
using Microsoft.EntityFrameworkCore;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Blockchain.Persistency.EntityFramework;

public class BlockchainStateRepository : RepositoryBase<BlockchainDbContext>, IBlockchainStateRepository
{
    public async Task<BlockchainState> GetCurrentStateAsync() => 
        await this.Context.BlockchainStates.SingleOrDefaultAsync() ?? new GenesisBlockchainState();

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
