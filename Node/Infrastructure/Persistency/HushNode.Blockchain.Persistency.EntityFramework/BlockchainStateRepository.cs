using HushNode.Blockchain.Persistency.Abstractions.Models;
using HushNode.Blockchain.Persistency.Abstractions.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HushNode.Blockchain.Persistency.EntityFramework;

public class BlockchainStateRepository(BlockchainDbContext dbContext) : IBlockchainStateRepository
{
    private readonly BlockchainDbContext _dbContext = dbContext;

    public async Task<BlockchainState> GetCurrentStateAsync() => 
        await this._dbContext.BlockchainStates.SingleOrDefaultAsync() is BlockchainState currentBlockchainState
            ? currentBlockchainState
            : new GenesisBlockchainState();

    public async Task SetBlockchainStateAsync(BlockchainState blockchainState)
    {
        var currentBlockchainStateExists = await this._dbContext.BlockchainStates.AnyAsync();

        if (currentBlockchainStateExists)
        {
            this._dbContext.BlockchainStates.Attach(blockchainState);
            this._dbContext.Entry(blockchainState).State = EntityState.Modified;
        }
        else
        {
            await this._dbContext.BlockchainStates.AddAsync(blockchainState);
        }
    }

    public void AttachBlockchainState(BlockchainState blockchainState)
    {
        this._dbContext.BlockchainStates.Attach(blockchainState);
    }
}
