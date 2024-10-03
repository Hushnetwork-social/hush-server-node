using HushEcosystem.Model.Blockchain;

namespace HushServerNode.InternalModule.Authentication;

public interface IAuthenticationService
{
    HushUserProfile? GetUserProfile(string publicAddress);

    HushUserProfile? GetUserProfileByUserName(string UserName);

    Task UpdateProfile(HushUserProfile profile);
}
