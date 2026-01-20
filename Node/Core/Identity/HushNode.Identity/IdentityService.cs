using HushNode.Caching;
using HushNode.Identity.Storage;
using HushShared.Identity.Model;

namespace HushNode.Identity;

public class IdentityService(
    IIdentityStorageService identityStorageService,
    IIdentityCacheService identityCacheService) : IIdentityService
{
    private readonly IIdentityStorageService _identityStorageService = identityStorageService;
    private readonly IIdentityCacheService _identityCacheService = identityCacheService;

    public async Task<ProfileBase> RetrieveIdentityAsync(string publicSigningAddress)
    {
        // Cache-aside pattern: check cache first
        var cachedProfile = await this._identityCacheService.GetIdentityAsync(publicSigningAddress);
        if (cachedProfile != null)
        {
            return cachedProfile;
        }

        // Cache miss - query PostgreSQL
        var profile = await this._identityStorageService.RetrieveIdentityAsync(publicSigningAddress);

        // Only cache existing profiles (not NonExistingProfile)
        if (profile is Profile existingProfile)
        {
            await this._identityCacheService.SetIdentityAsync(publicSigningAddress, existingProfile);
        }

        return profile;
    }
}
