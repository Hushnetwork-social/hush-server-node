using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace HushServerNode.CacheService;

public class BlockchainCache : IBlockchainCache  
{
    private readonly BlockchainDataContext _blockchainDataContext;

    public BlockchainCache(BlockchainDataContext blockchainDataContext)
    {
        this._blockchainDataContext = blockchainDataContext;
    }

    public void ConnectPersistentDatabase()
    {
        this._blockchainDataContext.Database.EnsureCreated();
        this._blockchainDataContext.Database.Migrate();
    }

    public async Task<BlockchainState> GetBlockchainStateAsync()
    {
        return await this._blockchainDataContext.BlockchainState.SingleOrDefaultAsync();
    }

    public async Task UpdateBlockchainState(BlockchainState blockchainState)
    {
        if (this._blockchainDataContext.BlockchainState.Any())
        {
            this._blockchainDataContext.BlockchainState.Update(blockchainState);
        }
        else
        {
            this._blockchainDataContext.BlockchainState.Add(blockchainState);
        }

        await this._blockchainDataContext.SaveChangesAsync();
    }
}
