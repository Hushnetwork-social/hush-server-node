using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HushEcosystem.Model.Blockchain;
using HushEcosystem.Model.Builders;
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

            var feeds = this._blockchainIndexDb.Feeds.Where(x => feedIds.Contains(x.Id));

            if (feeds.Any(x => x.FeedType == FeedTypeEnum.Personal))
            {
                // already have a personal feed
                var personalFeed = feeds.Single(x => x.FeedType == FeedTypeEnum.Personal);
                personalFeed.FeedTitle = feedName;
            }
            {
                // don't have personal feed
                var newPersonalFeed = new PersonalFeedDefinition
                {
                    FeedId = feed.FeedId,
                    FeedOwner = feed.FeedParticipantPublicAddress,
                    FeedTitle = feedName,
                    BlockIndex = blockIndex
                };

                this._blockchainIndexDb.Feeds.Add(newPersonalFeed);
                this._blockchainIndexDb.FeedsOfParticipant.Add(feed.FeedParticipantPublicAddress, new List<string> { feed.FeedId });
            }
        }
        else
        {
            // user has no feeds. Create this one.
            var newPersonalFeed = new PersonalFeedDefinition
            {
                FeedId = feed.FeedId,
                FeedOwner = feed.FeedParticipantPublicAddress,
                FeedTitle = feedName,
                BlockIndex = blockIndex
            };

            this._blockchainIndexDb.Feeds.Add(newPersonalFeed);
            this._blockchainIndexDb.FeedsOfParticipant.Add(feed.FeedParticipantPublicAddress, new List<string> { feed.FeedId });
        }
        
        

        // // Check if there is already a Personal Feed for the user
        // var hasPersonalFeed = this._blockchainIndexDb.Feeds.Values
        //     .SelectMany(x => x)
        //     .Where(x => x.FeedType == FeedTypeEnum.Personal)
        //     .Any();

        // if (hasPersonalFeed)
        // {
        //     var personalFeed = this._blockchainIndexDb.Feeds[feed.FeedParticipantPublicAddress]
        //         .Single(x => x.FeedType == FeedTypeEnum.Personal);
            
        //     personalFeed.FeedTitle = feedName;
        //     personalFeed.BlockIndex = blockIndex;
        // }
        // else
        // {
        //     var newPersonalFeed = new PersonalFeedDefinition
        //     {
        //         FeedId = feed.FeedId,
        //         FeedOwner = feed.FeedParticipantPublicAddress,
        //         FeedTitle = feedName,
        //         BlockIndex = blockIndex
        //     };

        //     this._blockchainIndexDb.Feeds.Add(feed.FeedId, new List<IFeedDefinition> { newPersonalFeed });
        //     this._blockchainIndexDb.FeedsOfParticipant.Add(feed.FeedParticipantPublicAddress, new List<string> { feed.FeedId });
        // }
    }
}
