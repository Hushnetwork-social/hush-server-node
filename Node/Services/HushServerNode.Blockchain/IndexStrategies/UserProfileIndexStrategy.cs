using System.Threading.Tasks;
using HushEcosystem.Model.Blockchain;
using HushServerNode.Blockchain.ExtensionMethods;
using HushServerNode.CacheService;

namespace HushServerNode.Blockchain.IndexStrategies;

public class UserProfileIndexStrategy : IIndexStrategy
{
    private readonly IBlockchainCache _blockchainCache;

    public UserProfileIndexStrategy(IBlockchainCache blockchainCache)
    {
        this._blockchainCache = blockchainCache;
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
        await this._blockchainCache.UpdateProfile(userProfile.ToProfile());
    }
}
