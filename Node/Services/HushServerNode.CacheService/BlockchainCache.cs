using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace HushServerNode.CacheService;

public class BlockchainCache : IBlockchainCache  
{
    private readonly IDbContextFactory<BlockchainDataContext> _contextFactory;

    public BlockchainCache(IDbContextFactory<BlockchainDataContext> contextFactory)
    {
        this._contextFactory = contextFactory;
    }

    public void ConnectPersistentDatabase()
    {
        using var context = this._contextFactory.CreateDbContext();
        context.Database.EnsureCreated();
        // this._blockchainDataContext.Database.Migrate();
    }

    public async Task<BlockchainState> GetBlockchainStateAsync()
    {
        using var context = _contextFactory.CreateDbContext();
        return await context.BlockchainState.SingleOrDefaultAsync();
    }

    public async Task UpdateBlockchainState(BlockchainState blockchainState)
    {
        using var context = _contextFactory.CreateDbContext();
        if (context.BlockchainState.Any())
        {
            context.BlockchainState.Update(blockchainState);
        }
        else
        {
            context.BlockchainState.Add(blockchainState);
        }

        await context.SaveChangesAsync();
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

        using (var context = _contextFactory.CreateDbContext())
        {
            context.BlockEntities.Add(block);
            await context.SaveChangesAsync();
        }
    }

    public async Task UpdateBalanceAsync(string address, double value)
    {
        using (var context = _contextFactory.CreateDbContext())
        {
            var addressBalance = context.AddressesBalance
                .SingleOrDefault(a => a.Address == address);

            if (addressBalance == null)
            {
                addressBalance = new AddressBalance
                {
                    Address = address,
                    Balance = value
                };

                context.AddressesBalance.Add(addressBalance);
            }
            else
            {
                addressBalance.Balance += value;
                context.AddressesBalance.Update(addressBalance);
            }

            await context.SaveChangesAsync();
        }
    }

    public double GetBalance(string address)
    {
        using var context = _contextFactory.CreateDbContext();
        var addressBalance = context.AddressesBalance
            .SingleOrDefault(a => a.Address == address);

        if (addressBalance == null)
        {
            return 0;
        }

        return addressBalance.Balance;
    }

    public async Task UpdateProfile(Profile profile)
    {
        using (var context = _contextFactory.CreateDbContext())
        {
            var profileEntity = context.Profiles
                .SingleOrDefault(p => p.PublicSigningAddress == profile.PublicSigningAddress);

            if (profileEntity == null)
            {
                context.Profiles.Add(profile);
            }
            else
            {
                // TOOD [AboimPinto]: The system should only update the profile if the profile has changed in order to avoid unnecessary writes to the database.
                context.Profiles.Update(profile);
            }

            await context.SaveChangesAsync();
        }
    }

    public Profile? GetProfile(string address)
    {
        using var context = _contextFactory.CreateDbContext();
        var profile = context.Profiles
            .SingleOrDefault(p => p.PublicSigningAddress == address);

        return profile;
    }

    public FeedEntity? GetFeed(string feedId)
    {
        using var context = _contextFactory.CreateDbContext();
        return context.FeedEntities
            .SingleOrDefault(f => f.FeedId == feedId);
    }

    public bool FeedExists(string feedId)
    {
        using var context = _contextFactory.CreateDbContext();
        return context.FeedEntities
            .Any(f => f.FeedId == feedId);
    }

    public bool UserHasFeeds(string address)
    {
        using var context = _contextFactory.CreateDbContext();
        return context.FeedParticipants
            .Any(x =>
                x.ParticipantPublicAddress == address &&
                x.ParticipantType == 0);
    }

    public bool UserHasPersonalFeed(string address)
    {
        using (var context = _contextFactory.CreateDbContext())
        {
            return context.FeedParticipants
                .Any(x => 
                    x.ParticipantPublicAddress == address &&
                    x.ParticipantType == 0 && 
                    x.Feed.FeedType == 0);
        }
    }

    public async Task CreatePersonalFeed(
        string feedTitle,
        string feedId,
        int feedType,
        string personalFeedOwnerAddress,
        string publicEncryptAddress,
        string privateEncryptKey,
        double blockIndex)
    {
        // TODO [AboimPinto]: The meaning of the BlockIndex could have several meanings here:
        // 1. In the FeedEntity, the BlockIndex is the block index of the first block of the feed.
        // 2. In the FeedParticipants, the BlockIndex is the block index since when the credentials are valid
        // NOTE: If a participant is removed from the feed, for all other participant a new valid credentials are created. 
        // How the user know that was kicked out of the feed?

        var participantFeedEntity = new FeedParticipants
        {
            FeedId = feedId,
            ParticipantPublicAddress = personalFeedOwnerAddress,
            ParticipantType = 0,
            PublicEncryptAddress = publicEncryptAddress,
            PrivateEncryptKey = privateEncryptKey
        };

        var personlFeedEntity = new FeedEntity
        {
            FeedId = feedId,
            FeedType = feedType,
            Title = feedTitle,
            BlockIndex = blockIndex,
            FeedParticipants = new List<FeedParticipants> { participantFeedEntity },
        };

        using var context = _contextFactory.CreateDbContext();
        context.FeedEntities.Add(personlFeedEntity);
        await context.SaveChangesAsync();
    }

    public async Task CreateChatFeed(
        string feedId,
        int feedType,
        string chatParticipantAddress,
        string chatParticipantPublicEncryptAddress,
        string chatParticipantPrivateEncryptKey,
        double blockIndex)
    {
        // TODO [AboimPinto]: The meaning of the BlockIndex could have several meanings here:
        // 1. In the FeedEntity, the BlockIndex is the block index of the first block of the feed.
        // 2. In the FeedParticipants, the BlockIndex is the block index since when the credentials are valid
        // NOTE: If a participant is removed from the feed, for all other participant a new valid credentials are created. 
        // How the user know that was kicked out of the feed?

        var chatParticipantFeedEntity = new FeedParticipants
        {
            FeedId = feedId,
            ParticipantPublicAddress = chatParticipantAddress,
            ParticipantType = 0,
            PublicEncryptAddress = chatParticipantPublicEncryptAddress,
            PrivateEncryptKey = chatParticipantPrivateEncryptKey
        };

        var personlFeedEntity = new FeedEntity
        {
            FeedId = feedId,
            FeedType = feedType,
            Title = "n/a",
            BlockIndex = blockIndex,
            FeedParticipants = new List<FeedParticipants> 
            { 
                chatParticipantFeedEntity
            }, 
        };

        using var context = _contextFactory.CreateDbContext();
        context.FeedEntities.Add(personlFeedEntity);
        await context.SaveChangesAsync();
    }

    public async Task AddParticipantToChatFeed(
        string feedId,
        string chatParticipantAddress,
        string chatParticipantPublicEncryptAddress,
        string chatParticipantPrivateEncryptKey,
        double blockIndex)
    {
        var chatParticipantFeedEntity = new FeedParticipants
        {
            FeedId = feedId,
            ParticipantPublicAddress = chatParticipantAddress,
            ParticipantType = 0,
            PublicEncryptAddress = chatParticipantPublicEncryptAddress,
            PrivateEncryptKey = chatParticipantPrivateEncryptKey
        };

        using var context = _contextFactory.CreateDbContext();
        context.FeedParticipants.Add(chatParticipantFeedEntity);
        await context.SaveChangesAsync();
    }

    public IEnumerable<FeedEntity> GetUserFeeds(string address)
    {
        using var context = _contextFactory.CreateDbContext();
        return context.FeedEntities
            .Include(x => x.FeedParticipants)
            .Where(x => x.FeedParticipants.Any(p => p.ParticipantPublicAddress == address))
            .ToList();
    }

    public IEnumerable<FeedMessageEntity> GetFeedMessages(string feedId, double blockIndex)
    {
        using var context = this._contextFactory.CreateDbContext();
        return context.FeedMessages
            .Where(x => x.FeedId == feedId && x.BlockIndex > blockIndex)
            .ToList();
    }

    public async Task SaveMessageAsync(FeedMessageEntity feedMessage)
    {
        using var context = this._contextFactory.CreateDbContext();
        context.FeedMessages.Add(feedMessage);
        await context.SaveChangesAsync();
    }
}
