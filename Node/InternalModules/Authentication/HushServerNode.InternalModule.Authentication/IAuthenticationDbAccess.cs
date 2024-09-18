using HushServerNode.InternalModule.Authentication.Cache;

namespace HushServerNode.InternalModule.Authentication;

public interface IAuthenticationDbAccess
{
    Profile? GetProfile(string address);

    Task AddProfile(Profile profile);

    Task UpdateProfile(Profile profile);
}