using HushEcosystem.Model.Blockchain;
using HushServerNode.InternalModule.Authentication.Cache;

namespace HushServerNode.InternalModule.Authentication;

public static class HushUserProfileExtensions
{
    public static HushUserProfile ToHushUserProfile(this Profile profileEntity)
    {
        return new HushUserProfile
        {
            UserName = profileEntity.UserName,
            UserPublicSigningAddress = profileEntity.PublicSigningAddress,
            UserPublicEncryptAddress = profileEntity.PublicEncryptAddress,
            IsPublic = profileEntity.IsPublic
        };
    }
}
