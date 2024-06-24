using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HushEcosystem.Model.Blockchain;
using HushEcosystem.Model.Rpc.Feeds;

namespace HushServerNode.Blockchain.IndexStrategies;

public class FeedIndexStrategy : IIndexStrategy
{
    private readonly IBlockchainIndexDb _blockchainIndexDb;

    public FeedIndexStrategy(IBlockchainIndexDb blockchainIndexDb)
    {
        this._blockchainIndexDb = blockchainIndexDb;
    }

    public bool CanHandle(VerifiedTransaction verifiedTransaction)
    {
        if (verifiedTransaction.SpecificTransaction.Id == Feed.TypeCode)
        {
            return true;
        }
 
        return false;
    }

    public Task Handle(VerifiedTransaction verifiedTransaction)
    {
        var feed = (Feed)verifiedTransaction.SpecificTransaction;

        switch(feed.FeedType)
        {
            case FeedTypeEnum.Personal:
                this.HandlesPersonalFeed(feed, verifiedTransaction.BlockIndex);
                break;
            case FeedTypeEnum.Chat:
                this.HandlesChatFeed(feed, verifiedTransaction.BlockIndex);
                break;
        }

        return Task.CompletedTask;
    }

    private void HandlesPersonalFeed(Feed feed, double blockIndex)
    {
        var feedParticipantProfile = this._blockchainIndexDb.Profiles.SingleOrDefault(x => x.Issuer == feed.Issuer);
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
        var userHasFeeds = this._blockchainIndexDb.FeedsOfParticipant.ContainsKey(feed.FeedParticipantPublicAddress);

        if (userHasFeeds)
        {
            // list all feed for the user
            var feedIds = this._blockchainIndexDb.FeedsOfParticipant[feed.FeedParticipantPublicAddress];

            var feeds = this._blockchainIndexDb.Feeds.Where(x => feedIds.Contains(x.FeedId));

            var hasPersonalFeed = feeds.Any(x => x.FeedType == FeedTypeEnum.Personal);
            if (hasPersonalFeed)
            {
                // already have a personal feed
                var personalFeed = feeds.Single(x => x.FeedType == FeedTypeEnum.Personal);
                personalFeed.FeedTitle = feedName;
            }
            else
            {
                // don't have personal feed
                var newPersonalFeed = new PersonalFeedDefinition
                {
                    FeedId = feed.FeedId,
                    FeedParticipant = feed.FeedParticipantPublicAddress,
                    FeedTitle = feedName,
                    BlockIndex = blockIndex
                };

                this._blockchainIndexDb.Feeds.Add(newPersonalFeed);
                this._blockchainIndexDb.FeedsOfParticipant.Add(feed.FeedParticipantPublicAddress, new List<string> { feed.FeedId });
                this._blockchainIndexDb.ParticipantsOfFeed.Add(feed.FeedId, new List<string> { feed.FeedParticipantPublicAddress });
            }
        }
        else
        {
            // user has no feeds. Create this one.
            var newPersonalFeed = new PersonalFeedDefinition
            {
                FeedId = feed.FeedId,
                FeedParticipant = feed.FeedParticipantPublicAddress,
                FeedTitle = feedName,
                BlockIndex = blockIndex
            };

            this._blockchainIndexDb.Feeds.Add(newPersonalFeed);
            this._blockchainIndexDb.FeedsOfParticipant.Add(feed.FeedParticipantPublicAddress, new List<string> { feed.FeedId });
            this._blockchainIndexDb.ParticipantsOfFeed.Add(feed.FeedId, new List<string> { feed.FeedParticipantPublicAddress });
        }
    }

    private void HandlesChatFeed(Feed feed, double blockIndex)
    {
        // check if the feed already existis
        var feedExists = this._blockchainIndexDb.Feeds.Any(x => x.FeedId == feed.FeedId);

        if (feedExists)
        {
            // get the other participant of the feed if is already in the Blockchain
            var feedParticipants = this._blockchainIndexDb.FeedsOfParticipant
                .Where(x => x.Value.Contains(feed.FeedId))
                .Select(x => x.Key);

            if (feedParticipants.Contains(feed.FeedParticipantPublicAddress))
            {
                // participant is already in the feed as participant
            }
            else
            {
                // update the tittle of the other participant title
                var otherFeedDefinition = this._blockchainIndexDb.Feeds
                    .OfType<ChatFeedDefinition>()
                    .Single(x => x.FeedId == feed.FeedId);
                var otherParticipantProfile = this._blockchainIndexDb.Profiles
                    .SingleOrDefault(x => x.UserPublicSigningAddress == otherFeedDefinition.FeedParticipant);
                if(otherParticipantProfile != null)
                {
                    otherFeedDefinition.FeedTitle = otherParticipantProfile.UserName;
                }

                var thisParticipantProfile = this._blockchainIndexDb.Profiles
                    .Single(x => x.UserPublicSigningAddress == feedParticipants.Single());

                // add the new participant in the feed 
                var newChatFeed = new ChatFeedDefinition
                {
                    FeedId = feed.FeedId,
                    FeedParticipant = feed.FeedParticipantPublicAddress,
                    FeedTitle = thisParticipantProfile.UserName,
                    BlockIndex = blockIndex
                };

                this._blockchainIndexDb.Feeds.Add(newChatFeed);
                // TODO [AboimPinto] assuming the participant already has a Personal Feed.

                if (this._blockchainIndexDb.FeedsOfParticipant.ContainsKey(feed.FeedParticipantPublicAddress))
                {
                    this._blockchainIndexDb.FeedsOfParticipant[feed.FeedParticipantPublicAddress].Add(feed.FeedId);
                }
                else
                {
                    this._blockchainIndexDb.FeedsOfParticipant.Add(feed.FeedParticipantPublicAddress, new List<string> { feed.FeedId });
                }

                if (this._blockchainIndexDb.ParticipantsOfFeed.ContainsKey(feed.FeedParticipantPublicAddress))
                {
                    this._blockchainIndexDb.ParticipantsOfFeed[feed.FeedId].Add(feed.FeedParticipantPublicAddress);
                }
                else
                {
                    this._blockchainIndexDb.ParticipantsOfFeed.Add(feed.FeedId, new List<string> { feed.FeedParticipantPublicAddress});
                }
            }
        }
        else
        {
            // feed doesn't exist. Tipical situation, it's a new feed
            var newChatFeed = new ChatFeedDefinition
                {
                    FeedId = feed.FeedId,
                    FeedParticipant = feed.FeedParticipantPublicAddress,
                    FeedTitle = "Anonymous",
                    BlockIndex = blockIndex
                };

                this._blockchainIndexDb.Feeds.Add(newChatFeed);
                
                if (this._blockchainIndexDb.FeedsOfParticipant.ContainsKey(feed.FeedParticipantPublicAddress))
                {
                    this._blockchainIndexDb.FeedsOfParticipant[feed.FeedParticipantPublicAddress].Add(feed.FeedId);
                }
                else
                {
                    this._blockchainIndexDb.FeedsOfParticipant.Add(feed.FeedParticipantPublicAddress, new List<string> { feed.FeedId });
                }
        }
        
    }
}
