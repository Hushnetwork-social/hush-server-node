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

    public async Task SaveBlockAsync(
        string blockId, 
        long blockHeight, 
        string previousBlockId, 
        string nextBlockId, 
        string blockHash, 
        string blockJson)
    {
        var block = new BlockEntity
        {
            BlockId = blockId,
            Height = blockHeight,
            PreviousBlockId = previousBlockId,
            NextBlockId = nextBlockId,
            Hash = blockHash,
            BlockJson = blockJson
        };

        this._blockchainDataContext.BlockEntities.Add(block);
        await this._blockchainDataContext.SaveChangesAsync();
    }

    public async Task UpdateBalanceAsync(string address, double value)
    {
        var addressBalance = this._blockchainDataContext.AddressesBalance
            .SingleOrDefault(a => a.Address == address);

        if (addressBalance == null)
        {
            addressBalance = new AddressBalance
            {
                Address = address,
                Balance = value
            };

            this._blockchainDataContext.AddressesBalance.Add(addressBalance);
        }
        else
        {
            addressBalance.Balance += value;
            this._blockchainDataContext.AddressesBalance.Update(addressBalance);
        }

        await this._blockchainDataContext.SaveChangesAsync();
    }

    public double GetBalance(string address)
    {
        var addressBalance = this._blockchainDataContext.AddressesBalance
            .SingleOrDefault(a => a.Address == address);

        if (addressBalance == null)
        {
            return 0;
        }

        return addressBalance.Balance;
    }

    public async Task UpdateProfile(Profile profile)
    {
        var profileEntity = this._blockchainDataContext.Profiles
            .SingleOrDefault(p => p.PublicSigningAddress == profile.PublicSigningAddress);

        if (profileEntity == null)
        {
            this._blockchainDataContext.Profiles.Add(profile);
        }
        else
        {
            // TOOD [AboimPinto]: The system should only update the profile if the profile has changed in order to avoid unnecessary writes to the database.
            this._blockchainDataContext.Profiles.Update(profile);
        }

        await this._blockchainDataContext.SaveChangesAsync();
    }

    public Profile GetProfile(string address)
    {
        var profile = this._blockchainDataContext.Profiles
            .SingleOrDefault(p => p.PublicSigningAddress == address);

        return profile;
    }
}
