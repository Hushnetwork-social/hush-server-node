using HushShared.Identity.Model;

namespace HushNode.Identity.Storage;

/// <summary>
/// Service for retrieving identity information with caching support.
/// </summary>
public interface IIdentityService
{
    Task<ProfileBase> RetrieveIdentityAsync(string publicSigningAddress);
}
