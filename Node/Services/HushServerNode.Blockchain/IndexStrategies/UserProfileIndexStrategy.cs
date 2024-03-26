using System.Linq;
using System.Threading.Tasks;
using HushEcosystem.Model.Blockchain;

namespace HushServerNode.Blockchain.IndexStrategies;

public class UserProfileIndexStrategy : IIndexStrategy
{
    private readonly IBlockchainIndexDb _blockchainIndexDb;
        
    public UserProfileIndexStrategy(IBlockchainIndexDb blockchainIndexDb)
    {
        this._blockchainIndexDb = blockchainIndexDb;
    }

    public bool CanHandle(VerifiedTransaction verifiedTransaction)
    {
        if (verifiedTransaction.SpecificTransaction.Id == UserProfile.TransactionGuid)
        {
            return true;
        }

        return false;
    }

    public Task Handle(VerifiedTransaction verifiedTransaction)
    {
        var userProfile = (UserProfile)verifiedTransaction.SpecificTransaction;

        var existingProfile = this._blockchainIndexDb.Profiles.SingleOrDefault(x => x.UserPublicSigningAddress == userProfile.UserPublicSigningAddress);
        if (existingProfile == null)
        {
            this._blockchainIndexDb.Profiles.Add(userProfile);
        }
        else
        {
            existingProfile.UserName = userProfile.UserName;
            existingProfile.IsPublic = userProfile.IsPublic;
        }

        return Task.CompletedTask;
    }
}
