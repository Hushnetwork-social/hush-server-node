using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HushEcosystem.Model.Blockchain;
using HushEcosystem.Model.Rpc.Feeds;
using HushServerNode.CacheService;

namespace HushServerNode.Blockchain.IndexStrategies;

public class FeedMessageIndexStrategy : IIndexStrategy
{
    private readonly IBlockchainCache _blockchainCache;

    public FeedMessageIndexStrategy(IBlockchainCache blockchainCache)
    {
        this._blockchainCache = blockchainCache;
    }

    public bool CanHandle(VerifiedTransaction verifiedTransaction)
    {
        if (verifiedTransaction.SpecificTransaction.Id == FeedMessage.TypeCode)
        {
            return true;
        }
 
        return false;
    }

    public async Task Handle(VerifiedTransaction verifiedTransaction)
    {
        var feedMessage = (FeedMessage)verifiedTransaction.SpecificTransaction;

        var messageIssueProfile = this._blockchainCache.GetProfile(feedMessage.Issuer);
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
            BlockIndex = verifiedTransaction.BlockIndex
        };

        await this._blockchainCache.SaveMessageAsync(newFeedMessage);

        // // Check if the message is for a valid Feed
        // // TODO [AboimPinto] Need to check this LINQ for High performance.
        // // if (!this._blockchainIndexDb.Feeds.Values
        // //     .SelectMany(x => x)
        // //     .Where(x => x.FeedId == feedMessage.FeedId)
        // //     .Any())
        // if (!this._blockchainIndexDb.Feeds.Any(x => x.FeedId == feedMessage.FeedId))
        // {
        //     // need to report the node that validated this message. It is not valid for the feed.
        //     return Task.CompletedTask;    
        // }

        // // var  issuerProfile = this._blockchainIndexDb.Profiles.SingleOrDefault(x => x.Issuer == feedMessage.Issuer);
        // var  issuerProfile = this._blockchainCache.GetProfile(feedMessage.Issuer);
        // var profileName = feedMessage.Issuer.Substring(0, 10);
        // if (issuerProfile != null)
        // {
        //     profileName = issuerProfile.UserName;
        // }

        // var feedMessageDefinition = new FeedMessageDefinition
        // {
        //     FeedId = feedMessage.FeedId,
        //     FeedMessageId = feedMessage.FeedMessageId,
        //     MessageContent = feedMessage.Message,
        //     IssuerPublicAddress = feedMessage.Issuer,
        //     IssuerName = profileName,
        //     TimeStamp = feedMessage.TimeStamp,
        //     BlockIndex = verifiedTransaction.BlockIndex
        // };

        // if (this._blockchainIndexDb.FeedMessages.ContainsKey(feedMessage.FeedId))
        // {

        //     this._blockchainIndexDb.FeedMessages[feedMessage.FeedId].Add(feedMessageDefinition);
        // }
        // else
        // {
        //     this._blockchainIndexDb.FeedMessages.Add(feedMessage.FeedId, new List<FeedMessageDefinition> { feedMessageDefinition });
        // }


        // return Task.CompletedTask;
    }
}
