using HushServerNode.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HushServerNode.InternalModule.Authentication.Cache;

public class CacheAuthenticationDbContextConfigurator : IDbContextConfigurator
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<Profile>()
            .ToTable("AUTH_Profile")
            .HasKey(x => x.PublicSigningAddress);
    }
}
