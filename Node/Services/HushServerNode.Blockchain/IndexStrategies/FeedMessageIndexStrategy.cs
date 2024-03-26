using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HushEcosystem.Model.Blockchain;
using HushEcosystem.Model.Rpc.Feeds;

namespace HushServerNode.Blockchain.IndexStrategies;

public class FeedMessageIndexStrategy : IIndexStrategy
{
    private readonly IBlockchainIndexDb _blockchainIndexDb;

    public FeedMessageIndexStrategy(IBlockchainIndexDb blockchainIndexDb)
    {
        this._blockchainIndexDb = blockchainIndexDb;
    }

    public bool CanHandle(VerifiedTransaction verifiedTransaction)
    {
        if (verifiedTransaction.SpecificTransaction.Id == FeedMessage.TypeCode)
        {
            return true;
        }
 
        return false;
    }

    public Task Handle(VerifiedTransaction verifiedTransaction)
    {
        // var feedMessage = (FeedMessage)verifiedTransaction.SpecificTransaction;

        // // Check if the message is for a valid Feed
        // // TODO [AboimPinto] Need to check this LINQ for High performance.
        // if (!this._blockchainIndexDb.Feeds.Values
        //     .SelectMany(x => x)
        //     .Where(x => x.FeedId == feedMessage.FeedId)
        //     .Any())
        // {
        //     // need to report the node that validated this message. It is not valid for the feed.
        //     return Task.CompletedTask;    
        // }

        // var  issuerProfile = this._blockchainIndexDb.Profiles.SingleOrDefault(x => x.Issuer == feedMessage.Issuer);
        // var profileName = feedMessage.Issuer.Substring(0, 10);
        // if (issuerProfile != null)
        // {
        //     profileName = issuerProfile.UserName;
        // }

        // var feedMessageDefinition = new FeedMessageDefinition
        // {
        //     FeedId = feedMessage.FeedId,
        //     FeedMessageId = feedMessage.Id,
        //     MessageContent = feedMessage.Message,
        //     IssuerPublicAddress = feedMessage.Issuer,
        //     IssuerName = profileName,
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


        return Task.CompletedTask;
    }
}
