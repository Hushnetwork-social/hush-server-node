using HushEcosystem.Model.Blockchain;
using HushServerNode.Interfaces;
using HushServerNode.InternalModule.Feed;

namespace HushServerNode.Blockchain.IndexStrategies;

public class FeedMessageIndexStrategy : IIndexStrategy
{
    private readonly IFeedService _feedService;

    public FeedMessageIndexStrategy(IFeedService feedService)
    {
        this._feedService = feedService;
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

        await this._feedService.AddMessage(feedMessage, verifiedTransaction.BlockIndex);
    }
}
