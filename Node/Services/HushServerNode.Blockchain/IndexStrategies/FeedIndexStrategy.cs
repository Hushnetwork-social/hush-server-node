using System.Linq;
using System.Threading.Tasks;
using HushEcosystem.Model.Blockchain;
using HushServerNode.CacheService;

namespace HushServerNode.Blockchain.IndexStrategies;

public class FeedIndexStrategy : IIndexStrategy
{
    private readonly IBlockchainCache _blockchainCache;

    public FeedIndexStrategy(IBlockchainCache blockchainCache)
    {
        this._blockchainCache = blockchainCache;
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
            case (int)FeedTypeEnum.Personal:
                this.HandlesPersonalFeed(feed, verifiedTransaction.BlockIndex);
                break;
            case (int)FeedTypeEnum.Chat:
                this.HandlesChatFeed(feed, verifiedTransaction.BlockIndex);
                break;
        }

        return Task.CompletedTask;
    }

    private void HandlesPersonalFeed(Feed feed, double blockIndex)
    {
        // var feedParticipantProfile = this._blockchainIndexDb.Profiles.SingleOrDefault(x => x.Issuer == feed.Issuer);
        var feedParticipantProfile = this._blockchainCache.GetProfile(feed.Issuer);

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
        var userHasPesonalFeed = this._blockchainCache.UserHasPersonalFeed(feed.FeedParticipantPublicAddress);

        if (userHasPesonalFeed)
        {
            // Get personal feed
        }
        else
        {
            // Create personal feed
            this._blockchainCache.CreatePersonalFeed(
                feedName,
                feed.FeedId, 
                feed.FeedType, 
                feed.FeedParticipantPublicAddress, 
                feed.FeedPublicEncriptAddress,
                feed.FeedPrivateEncriptAddress,
                blockIndex);
        }
    }

    private async void HandlesChatFeed(Feed feed, double blockIndex)
    {
        // check if the feed already existis
        // var feedExists = this._blockchainIndexDb.Feeds.Any(x => x.FeedId == feed.FeedId);
        var feedExists = this._blockchainCache.FeedExists(feed.FeedId);

        if (feedExists)
        {
            var chatFeed = this._blockchainCache.GetFeed(feed.FeedId);
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
                    this._blockchainCache.AddParticipantToChatFeed(
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
            await this._blockchainCache.CreateChatFeed(
                feed.FeedId,
                feed.FeedType,
                feed.FeedParticipantPublicAddress, 
                feed.FeedPublicEncriptAddress,
                feed.FeedPrivateEncriptAddress,
                blockIndex);
        }

        // if (feedExists)
        // {
        //     // get the other participant of the feed if is already in the Blockchain
        //     var feedParticipants = this._blockchainIndexDb.FeedsOfParticipant
        //         .Where(x => x.Value.Contains(feed.FeedId))
        //         .Select(x => x.Key);

        //     if (feedParticipants.Contains(feed.FeedParticipantPublicAddress))
        //     {
        //         // participant is already in the feed as participant
        //     }
        //     else
        //     {
        //         // update the tittle of the other participant title
        //         var otherFeedDefinition = this._blockchainIndexDb.Feeds
        //             .OfType<ChatFeedDefinition>()
        //             .Single(x => x.FeedId == feed.FeedId);
                
        //         var otherParticipantProfile = this._blockchainCache.GetProfile(otherFeedDefinition.FeedParticipant);

        //         if(otherParticipantProfile != null)
        //         {
        //             otherFeedDefinition.FeedTitle = otherParticipantProfile.UserName;
        //         }

        //         var thisParticipantProfile = this._blockchainCache.GetProfile(feedParticipants.Single());

        //         // add the new participant in the feed 
        //         var newChatFeed = new ChatFeedDefinition
        //         {
        //             FeedId = feed.FeedId,
        //             FeedParticipant = feed.FeedParticipantPublicAddress,
        //             FeedTitle = thisParticipantProfile.UserName,
        //             BlockIndex = blockIndex
        //         };

        //         this._blockchainIndexDb.Feeds.Add(newChatFeed);
        //         // TODO [AboimPinto] assuming the participant already has a Personal Feed.

        //         if (this._blockchainIndexDb.FeedsOfParticipant.ContainsKey(feed.FeedParticipantPublicAddress))
        //         {
        //             this._blockchainIndexDb.FeedsOfParticipant[feed.FeedParticipantPublicAddress].Add(feed.FeedId);
        //         }
        //         else
        //         {
        //             this._blockchainIndexDb.FeedsOfParticipant.Add(feed.FeedParticipantPublicAddress, new List<string> { feed.FeedId });
        //         }

        //         if (this._blockchainIndexDb.ParticipantsOfFeed.ContainsKey(feed.FeedParticipantPublicAddress))
        //         {
        //             this._blockchainIndexDb.ParticipantsOfFeed[feed.FeedId].Add(feed.FeedParticipantPublicAddress);
        //         }
        //         else
        //         {
        //             this._blockchainIndexDb.ParticipantsOfFeed.Add(feed.FeedId, new List<string> { feed.FeedParticipantPublicAddress});
        //         }
        //     }
        // }
        // else
        // {
        //     // feed doesn't exist. Tipical situation, it's a new feed
        //     var newChatFeed = new ChatFeedDefinition
        //         {
        //             FeedId = feed.FeedId,
        //             FeedParticipant = feed.FeedParticipantPublicAddress,
        //             FeedTitle = "Anonymous",
        //             BlockIndex = blockIndex
        //         };

        //         this._blockchainIndexDb.Feeds.Add(newChatFeed);
                
        //         if (this._blockchainIndexDb.FeedsOfParticipant.ContainsKey(feed.FeedParticipantPublicAddress))
        //         {
        //             this._blockchainIndexDb.FeedsOfParticipant[feed.FeedParticipantPublicAddress].Add(feed.FeedId);
        //         }
        //         else
        //         {
        //             this._blockchainIndexDb.FeedsOfParticipant.Add(feed.FeedParticipantPublicAddress, new List<string> { feed.FeedId });
        //         }
        // }
    }
}
