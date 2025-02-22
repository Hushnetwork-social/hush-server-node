using HushEcosystem.Model.Blockchain;
using HushServerNode.Interfaces;

namespace HushServerNode.InternalModule.Authentication.IndexStrategies;

public class UserProfileIndexStrategy : IIndexStrategy
{
    private readonly IAuthenticationService _authenticationService;

    public UserProfileIndexStrategy(IAuthenticationService authenticationService)
    {
        this._authenticationService = authenticationService;
    }

    public bool CanHandle(VerifiedTransaction verifiedTransaction)
    {
        if (verifiedTransaction.SpecificTransaction.Id == HushUserProfile.TransactionGuid)
        {
            return true;
        }

        return false;
    }

    public async Task Handle(VerifiedTransaction verifiedTransaction)
    {
        var userProfile = (HushUserProfile)verifiedTransaction.SpecificTransaction;
        await this._authenticationService.UpdateProfile(userProfile);
    }
}
