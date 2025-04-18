using HushShared.Identity.Model;

namespace HushNode.Identity;

public interface IIdentityService
{
    Task<ProfileBase> RetrieveIdentityAsync(string publicSigingAddress);
}