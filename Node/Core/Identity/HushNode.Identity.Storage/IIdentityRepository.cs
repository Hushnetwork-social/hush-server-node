using HushNode.Identity.Model;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Identity.Storage;

public interface IIdentityRepository : IRepository
{
    Task<ProfileBase> GetIdentityAsync(string publicSigningAddress);
}
