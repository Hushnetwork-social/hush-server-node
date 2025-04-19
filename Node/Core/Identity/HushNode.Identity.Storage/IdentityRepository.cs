using Microsoft.EntityFrameworkCore;
using Olimpo.EntityFramework.Persistency;
using HushShared.Identity.Model;

namespace HushNode.Identity.Storage;

public class IdentityRepository : RepositoryBase<IdentityDbContext>, IIdentityRepository
{
    public async Task<bool> AnyAsync(string publicSigningAddress) => 
        await this.Context.Profiles
            .AnyAsync(x => x.PublicSigningAddress == publicSigningAddress);

    public async Task AddFullIdentity(Profile profile) => 
        await this.Context.AddAsync(profile);

    public async Task<ProfileBase> GetIdentityAsync(string publicSigningAddress) => 
        await this.Context.Profiles
            .SingleOrDefaultAsync(x => x.PublicSigningAddress == publicSigningAddress) ?? (ProfileBase)new NonExistingProfile();

    public async Task<IEnumerable<Profile>> SearchByDisplayNameAsync(string partialDisplayName) => 
        await this.Context.Profiles
            .Where(x => x.Alias.ToLower().Contains(partialDisplayName.ToLower()))
            .ToListAsync();
}
