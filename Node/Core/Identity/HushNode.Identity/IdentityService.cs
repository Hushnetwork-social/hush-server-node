using HushNode.Identity.Storage;
using HushShared.Identity.Model;

namespace HushNode.Identity;

public class IdentityService(IIdentityStorageService identityStorageService) : IIdentityService
{
    private readonly IIdentityStorageService _identityStorageService = identityStorageService;

    public Task<ProfileBase> RetrieveIdentityAsync(string publicSigingAddress) => 
        this._identityStorageService.RetrieveIdentityAsync(publicSigingAddress);
}
