using HushServerNode.InternalModule.Authentication.Cache;

namespace HushServerNode.InternalModule.Authentication;

public interface IAuthenticationDbAccess
{
    Profile? GetProfile(string address);

    Profile? GetProfileByUserName(string userName);

    Task AddProfile(Profile profile);

    Task UpdateProfile(Profile profile);
}