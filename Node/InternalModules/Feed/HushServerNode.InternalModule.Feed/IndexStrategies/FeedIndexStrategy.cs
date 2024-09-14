using HushEcosystem.Model.Blockchain;
using HushServerNode.Interfaces;

namespace HushServerNode.InternalModule.Feed.IndexStrategies;

public class FeedIndexStrategy : IIndexStrategy
{
    private readonly IFeedService _feedService;

    public FeedIndexStrategy(IFeedService feedService)
    {
        this._feedService = feedService;
    }

    public bool CanHandle(VerifiedTransaction verifiedTransaction)
    {
        if (verifiedTransaction.SpecificTransaction.Id == HushEcosystem.Model.Blockchain.Feed.TypeCode)
        {
            return true;
        }
 
        return false;
    }

    public async Task Handle(VerifiedTransaction verifiedTransaction)
    {
        var feed = (HushEcosystem.Model.Blockchain.Feed)verifiedTransaction.SpecificTransaction;

        await this._feedService.AddFeedAsync(feed, verifiedTransaction.BlockIndex);

        // switch(feed.FeedType)
        // {
        //     case (int)FeedTypeEnum.Personal:
        //         this.HandlesPersonalFeed(feed, verifiedTransaction.BlockIndex);
        //         break;
        //     case (int)FeedTypeEnum.Chat:
        //         this.HandlesChatFeed(feed, verifiedTransaction.BlockIndex);
        //         break;
        // }

        // return Task.CompletedTask;
    }

    private void HandlesPersonalFeed(HushEcosystem.Model.Blockchain.Feed feed, double blockIndex)
    {
        // var feedParticipantProfile = this._blockchainCache.GetProfile(feed.Issuer);

        // var feedName = string.Empty;
        // if (feedParticipantProfile == null)
        // {
        //     feedName = feed.FeedParticipantPublicAddress.Substring(0, 10);
        // }
        // else
        // {
        //     feedName = $"{feedParticipantProfile.UserName} (You)";
        // }

        // // check if the user has feeds 
        // var userHasPesonalFeed = this._blockchainCache.UserHasPersonalFeed(feed.FeedParticipantPublicAddress);

        // if (userHasPesonalFeed)
        // {
        //     // Get personal feed
        // }
        // else
        // {
        //     // Create personal feed
        //     this._blockchainCache.CreatePersonalFeed(
        //         feedName,
        //         feed.FeedId, 
        //         feed.FeedType, 
        //         feed.FeedParticipantPublicAddress, 
        //         feed.FeedPublicEncriptAddress,
        //         feed.FeedPrivateEncriptAddress,
        //         blockIndex);
        // }
    }

    private async void HandlesChatFeed(HushEcosystem.Model.Blockchain.Feed feed, double blockIndex)
    {
        // // check if the feed already existis
        // var feedExists = this._blockchainCache.FeedExists(feed.FeedId);

        // if (feedExists)
        // {
        //     var chatFeed = this._blockchainCache.GetFeed(feed.FeedId);
        //     if (chatFeed.FeedParticipants.Any(x => x.ParticipantPublicAddress == feed.FeedParticipantPublicAddress))
        //     {
        //         // participant is already in the feed as participant
        //     }
        //     else
        //     {
        //         if (chatFeed.FeedParticipants.Count == 2)
        //         {
        //             // This feed already have 2 participants
        //         }
        //         else
        //         {
        //             this._blockchainCache.AddParticipantToChatFeed(
        //                 feed.FeedId,
        //                 feed.FeedParticipantPublicAddress,
        //                 feed.FeedPublicEncriptAddress,
        //                 feed.FeedPrivateEncriptAddress,
        //                 blockIndex);
        //         }
        //     }

        // }
        // else
        // {
        //     await this._blockchainCache.CreateChatFeed(
        //         feed.FeedId,
        //         feed.FeedType,
        //         feed.FeedParticipantPublicAddress, 
        //         feed.FeedPublicEncriptAddress,
        //         feed.FeedPrivateEncriptAddress,
        //         blockIndex);
        // }
    }
}
