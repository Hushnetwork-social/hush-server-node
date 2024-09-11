using HushServerNode.InternalModule.Authentication.Cache;
using Microsoft.EntityFrameworkCore;

namespace HushServerNode.InternalModule.Authentication;

public class AuthenticationDbAccess : IAuthenticationDbAccess
{
    private readonly IDbContextFactory<CacheAuthenticationDbContext> _dbContextFactory;

    public AuthenticationDbAccess(IDbContextFactory<CacheAuthenticationDbContext> dbContextFactory)
    {
        this._dbContextFactory = dbContextFactory;
    }

    public Profile? GetProfile(string address)
    {
        using var context = this._dbContextFactory.CreateDbContext();
        var profile = context.Profiles
            .SingleOrDefault(p => p.PublicSigningAddress == address);

        return profile;
    }
}
