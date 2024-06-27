using HushEcosystem.Model.Blockchain;
using HushServerNode.CacheService;

namespace HushServerNode.Blockchain.ExtensionMethods;

public static class HushUserProfileExtensions
{
    public static Profile ToProfile(this HushUserProfile hushUserProfile)
    {
        return new Profile
        {
            PublicSigningAddress = hushUserProfile.UserPublicSigningAddress,
            PublicEncryptAddress = hushUserProfile.UserPublicEncryptAddress,
            UserName = hushUserProfile.UserName,
            IsPublic = hushUserProfile.IsPublic,
        };
    }
}
