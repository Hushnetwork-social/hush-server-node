using HushEcosystem.Model.Bank;
using HushEcosystem.Model.Blockchain;
using HushServerNode.Interfaces;
using HushServerNode.InternalModule.Authentication;
using HushServerNode.InternalModule.Feed;

namespace HushServerNode.InternalModule.Bank.IndexStrategies;

public class TransferFundsIndexStrategy : IIndexStrategy
{
    private IBankService _bankService;
    private readonly IFeedService _feedService;
    private readonly IAuthenticationService _authenticationService;

    public TransferFundsIndexStrategy(
        IBankService bankService,
        IFeedService feedService,
        IAuthenticationService authenticationService)
    {
        this._bankService = bankService;
        this._feedService = feedService;
        this._authenticationService = authenticationService;
    }

    public bool CanHandle(VerifiedTransaction verifiedTransaction)
    {
        if (verifiedTransaction.SpecificTransaction is TransferFunds)
        {
            return true;
        }

        return false;
    }

    public async Task Handle(VerifiedTransaction verifiedTransaction)
    {
        var fundsTransfer = (TransferFunds) verifiedTransaction.SpecificTransaction;

        await this._bankService.UpdateFromAndToBalancesAsync(
            fundsTransfer.Issuer, 
            fundsTransfer.Value * -1, 
            fundsTransfer.ReceiverPublicAddress, 
            fundsTransfer.Value);

        var fromProfile = this._authenticationService.GetUserProfile(fundsTransfer.Issuer);
        var toProfile = this._authenticationService.GetUserProfile(fundsTransfer.ReceiverPublicAddress);

        var fromName = fromProfile == null ? fundsTransfer.Issuer.Substring(0, 10) : fromProfile.UserName;
        var toName = toProfile == null ? fundsTransfer.ReceiverPublicAddress.Substring(0, 10) : toProfile.UserName;

        var transferFundsMessage = $"{fundsTransfer.Value} HUSH transferred from {fromName} to {toName}";

        var feedMessage = new FeedMessage
        {
            FeedMessageId = Guid.NewGuid().ToString(),
            FeedId = fundsTransfer.FeedId,
            Issuer = fundsTransfer.Issuer,
            Message = Olimpo.EncryptKeys.Encrypt(transferFundsMessage, fundsTransfer.FeedPublicEncriptAddress),
            TimeStamp = DateTime.UtcNow,
        };

        await this._feedService.AddMessage(feedMessage, verifiedTransaction.BlockIndex);
    }
}
