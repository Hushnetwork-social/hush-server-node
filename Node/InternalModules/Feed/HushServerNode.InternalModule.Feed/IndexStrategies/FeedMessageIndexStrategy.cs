using HushEcosystem.Model.Blockchain;
using HushServerNode.Interfaces;
using HushServerNode.InternalModule.Authentication;
using HushServerNode.InternalModule.Feed;

namespace HushServerNode.Blockchain.IndexStrategies;

public class FeedMessageIndexStrategy : IIndexStrategy
{
    private readonly IFeedService _feedService;
    private readonly IAuthenticationService _authenticationService;

    public FeedMessageIndexStrategy(
        IFeedService feedService,
        IAuthenticationService authenticationService)
    {
        this._feedService = feedService;
        this._authenticationService = authenticationService;
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
