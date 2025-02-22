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
        
        return profileEntity.ToHushUserProfile();
    }

    public HushUserProfile? GetUserProfileByUserName(string UserName)
    {
        var profileEntity = this._authenticationDbAccess.GetProfileByUserName(UserName);

        if (profileEntity == null)
        {
            return default;
        }
        
        return profileEntity.ToHushUserProfile();
    }

    public async Task UpdateProfile(HushUserProfile profile)
    {
        var profileEntity = this._authenticationDbAccess.GetProfile(profile.UserPublicSigningAddress);

        if (profileEntity == null)
        {
            await this._authenticationDbAccess.AddProfile(profile.ToProfile());
        }
        else
        {
            // TOOD [AboimPinto]: The system should only update the profile if the profile has changed in order to avoid unnecessary writes to the database.
            await this._authenticationDbAccess.UpdateProfile(profile.ToProfile());
        }
    }
}
