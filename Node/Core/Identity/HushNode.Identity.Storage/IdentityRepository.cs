using Microsoft.EntityFrameworkCore;
using Olimpo.EntityFramework.Persistency;
using HushNode.Identity.Model;

namespace HushNode.Identity.Storage;

public class IdentityRepository : RepositoryBase<IdentityDbContext>, IIdentityRepository
{
    public async Task<ProfileBase> GetIdentityAsync(string publicSigningAddress) => 
        await this.Context.Profiles
            .SingleOrDefaultAsync(x => x.PublicSigningAddress == publicSigningAddress) ?? (ProfileBase)new NonExistingProfile();
}
