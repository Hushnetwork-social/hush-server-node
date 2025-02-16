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
    }
}
