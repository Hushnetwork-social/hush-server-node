using HushServerNode.InternalModule.Feed.Cache;
using Microsoft.EntityFrameworkCore;

namespace HushServerNode.InternalModule.Feed;

public class FeedDbAccess : IFeedDbAccess
{
    private readonly IDbContextFactory<CacheFeedDbContext> _dbContextFactory;

    public FeedDbAccess(IDbContextFactory<CacheFeedDbContext> dbContextFactory)
    {
        this._dbContextFactory = dbContextFactory;
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

        using var context = this._dbContextFactory.CreateDbContext();
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

        using var context = this._dbContextFactory.CreateDbContext();
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

        using var context = this._dbContextFactory.CreateDbContext();
        context.FeedParticipants.Add(chatParticipantFeedEntity);
        await context.SaveChangesAsync();
    }

    public bool UserHasPersonalFeed(string address)
    {
        using var context = this._dbContextFactory.CreateDbContext();
        return context.FeedParticipants
            .Any(x => 
                x.ParticipantPublicAddress == address &&
                x.ParticipantType == 0 && 
                x.Feed.FeedType == 0);
    }

    public bool FeedExists(string feedId)
    {
        using var context = this._dbContextFactory.CreateDbContext();
        return context.FeedEntities
            .Any(f => f.FeedId == feedId);
    }

    public FeedEntity? GetFeed(string feedId)
    {
        using var context = this._dbContextFactory.CreateDbContext();
        return context.FeedEntities
            .SingleOrDefault(f => f.FeedId == feedId);
    }

    public IEnumerable<FeedEntity> GetUserFeeds(string address)
    {
        using var context = this._dbContextFactory.CreateDbContext();
        return context.FeedEntities
            .Include(x => x.FeedParticipants)
            .Where(x => x.FeedParticipants.Any(p => p.ParticipantPublicAddress == address))
            .ToList();
    }

    public IEnumerable<FeedMessageEntity> GetFeedMessages(string feedId, double blockIndex)
    {
        using var context = this._dbContextFactory.CreateDbContext();
        return context.FeedMessages
            .Where(x => x.FeedId == feedId && x.BlockIndex > blockIndex)
            .ToList();
    }

    public async Task SaveMessageAsync(FeedMessageEntity feedMessage)
    {
        using var context = this._dbContextFactory.CreateDbContext();
        context.FeedMessages.Add(feedMessage);
        await context.SaveChangesAsync();
    }

    public bool UserHasFeeds(string address)
    {
        using var context = this._dbContextFactory.CreateDbContext();
        return context.FeedParticipants
            .Any(x =>
                x.ParticipantPublicAddress == address &&
                x.ParticipantType == 0);
    }
}
