using HushEcosystem.Model.Blockchain;
using HushServerNode.InternalModule.Authentication;
using HushServerNode.InternalModule.Feed.Cache;

namespace HushServerNode.InternalModule.Feed;

public class FeedService : IFeedService
{
    private readonly IAuthenticationService _authenticationService;
    private readonly IFeedDbAccess _feedDbAccess;

    public FeedService(
        IAuthenticationService authenticationService,
        IFeedDbAccess feedDbAccess)
    {
        this._authenticationService = authenticationService;
        this._feedDbAccess = feedDbAccess;
    }

    public async Task AddMessage(FeedMessage feedMessage, long blockIndex)
    {
        var messageIssueProfile = this._authenticationService.GetUserProfile(feedMessage.Issuer);
        var profileName = feedMessage.Issuer.Substring(0, 10);

        if (messageIssueProfile!= null)
        {
            profileName = messageIssueProfile.UserName;
        }

        var newFeedMessage = new FeedMessageEntity
        {
            FeedId = feedMessage.FeedId,
            FeedMessageId = feedMessage.FeedMessageId,
            MessageContent = feedMessage.Message,
            IssuerPublicAddress = feedMessage.Issuer,
            IssuerName = profileName,
            TimeStamp = feedMessage.TimeStamp,
            BlockIndex = blockIndex
        };

        await this._feedDbAccess.SaveMessageAsync(newFeedMessage);
    }

    public async Task AddFeedAsync(HushEcosystem.Model.Blockchain.Feed feed, long blockIndex)
    {
        switch(feed.FeedType)
        {
            case (int)FeedTypeEnum.Personal:
                await this.HandlesPersonalFeed(feed, blockIndex);
                break;
            case (int)FeedTypeEnum.Chat:
                await this.HandlesChatFeed(feed, blockIndex);
                break;
        }
    }

    public bool UserHasFeeds(string address)
    {
        return this._feedDbAccess.UserHasFeeds(address);
    }

    public IEnumerable<FeedEntity> GetUserFeeds(string address)
    {
        return this._feedDbAccess.GetUserFeeds(address);
    }

    public FeedEntity? GetFeed(string feedId)
    {
        return this._feedDbAccess.GetFeed(feedId);
    }

    public IEnumerable<FeedMessageEntity> GetFeedMessages(string feedId, double blockIndex)
    {
        return this._feedDbAccess.GetFeedMessages(feedId, blockIndex);
    }

    private async Task HandlesPersonalFeed(HushEcosystem.Model.Blockchain.Feed feed, double blockIndex)
    {
        var feedParticipantProfile = this._authenticationService.GetUserProfile(feed.Issuer);

        var feedName = string.Empty;
        if (feedParticipantProfile == null)
        {
            feedName = feed.FeedParticipantPublicAddress.Substring(0, 10);
        }
        else
        {
            feedName = $"{feedParticipantProfile.UserName} (You)";
        }

        // check if the user has feeds 
        var userHasPesonalFeed = this._feedDbAccess.UserHasPersonalFeed(feed.FeedParticipantPublicAddress);

        if (userHasPesonalFeed)
        {
            // Get personal feed
        }
        else
        {
            // Create personal feed
            await this._feedDbAccess.CreatePersonalFeed(
                feedName,
                feed.FeedId,
                feed.FeedType,
                feed.FeedParticipantPublicAddress,
                feed.FeedPublicEncriptAddress,
                feed.FeedPrivateEncriptAddress, 
                blockIndex);
        }
    }

    private async Task HandlesChatFeed(HushEcosystem.Model.Blockchain.Feed feed, double blockIndex)
    {
        // check if the feed already existis
        var feedExists = this._feedDbAccess.FeedExists(feed.FeedId);

        if (feedExists)
        {
            var chatFeed = this._feedDbAccess.GetFeed(feed.FeedId);
            if (chatFeed.FeedParticipants.Any(x => x.ParticipantPublicAddress == feed.FeedParticipantPublicAddress))
            {
                // participant is already in the feed as participant
            }
            else
            {
                if (chatFeed.FeedParticipants.Count == 2)
                {
                    // This feed already have 2 participants
                }
                else
                {
                    await this._feedDbAccess.AddParticipantToChatFeed(
                        feed.FeedId,
                        feed.FeedParticipantPublicAddress,
                        feed.FeedPublicEncriptAddress,
                        feed.FeedPrivateEncriptAddress,
                        blockIndex);
                }
            }

        }
        else
        {
            await this._feedDbAccess.CreateChatFeed(
                feed.FeedId,
                feed.FeedType,
                feed.FeedParticipantPublicAddress, 
                feed.FeedPublicEncriptAddress,
                feed.FeedPrivateEncriptAddress,
                blockIndex);
        }
    }
}
