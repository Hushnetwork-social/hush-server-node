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
}
