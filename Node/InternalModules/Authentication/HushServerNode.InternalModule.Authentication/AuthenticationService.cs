using HushEcosystem.Model.Blockchain;

namespace HushServerNode.InternalModule.Authentication;

public class AuthenticationService : IAuthenticationService
{
    private readonly IAuthenticationDbAccess _authenticationDbAccess;

    public AuthenticationService(IAuthenticationDbAccess authenticationDbAccess)
    {
        this._authenticationDbAccess = authenticationDbAccess;
    }

    public HushUserProfile? GetUserProfile(string publicAddress)
    {
        var profileEntity = this._authenticationDbAccess.GetProfile(publicAddress);

        if (profileEntity == null)
        {
            return default;
        }
        
        return new HushUserProfile
        {
            UserName = profileEntity.UserName,
            UserPublicSigningAddress = profileEntity.PublicSigningAddress,
            UserPublicEncryptAddress = profileEntity.PublicEncryptAddress,
            IsPublic = profileEntity.IsPublic
        };
    }
}
