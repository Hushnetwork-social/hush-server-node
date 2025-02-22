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

    public Profile? GetProfileByUserName(string userName)
    {
        using var context = this._dbContextFactory.CreateDbContext();
        var profile = context.Profiles
            .SingleOrDefault(p => p.UserName.ToLower().Contains(userName.ToLower()) && p.IsPublic);

        return profile;
    }

    public async Task AddProfile(Profile profile)
    {
        using var context = this._dbContextFactory.CreateDbContext();
        await context.Profiles.AddAsync(profile);

        await context.SaveChangesAsync();
    }

    public async Task UpdateProfile(Profile profile)
    {
        using var context = this._dbContextFactory.CreateDbContext();
        context.Profiles.Update(profile);

        await context.SaveChangesAsync();
    }
}
