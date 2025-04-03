using HushShared.Identity.Model;

namespace HushNode.Identity.Storage;

public interface IIdentityStorageService
{
    Task<ProfileBase> RetrieveIdentityAsync(string publicSigingAddress);
}