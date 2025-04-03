using HushShared.Identity.Model;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Identity.Storage;

public interface IIdentityRepository : IRepository
{
    Task<bool> AnyAsync(string publicSigningAddress);
    
    Task AddFullIdentity(Profile profile);

    Task<ProfileBase> GetIdentityAsync(string publicSigningAddress);
}
