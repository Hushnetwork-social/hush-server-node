using HushEcosystem.Model.Blockchain;
using HushServerNode.InternalModule.Authentication.Cache;

namespace HushServerNode.InternalModule.Authentication;

public static class ProfileExtensionMethods
{
    public static Profile ToProfile(this HushUserProfile hushUserProfile)
    {
        return new Profile
        {
            UserName = hushUserProfile.UserName,
            PublicSigningAddress = hushUserProfile.UserPublicSigningAddress,
            PublicEncryptAddress = hushUserProfile.UserPublicEncryptAddress,
            IsPublic = hushUserProfile.IsPublic
        };
    }
}
