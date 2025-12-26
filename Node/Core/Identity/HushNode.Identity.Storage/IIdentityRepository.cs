using HushShared.Blockchain.BlockModel;
using HushShared.Identity.Model;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Identity.Storage;

public interface IIdentityRepository : IRepository
{
    Task<bool> AnyAsync(string publicSigningAddress);
    
    Task AddFullIdentity(Profile profile);

    Task<ProfileBase> GetIdentityAsync(string publicSigningAddress);

    Task<IEnumerable<Profile>> SearchByDisplayNameAsync(string PartialDisplayName);

    Task UpdateAliasAsync(string publicSigningAddress, string newAlias, BlockIndex blockIndex);
}
