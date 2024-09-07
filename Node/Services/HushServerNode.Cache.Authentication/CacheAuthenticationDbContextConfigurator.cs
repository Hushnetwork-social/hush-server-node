using HushServerNode.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HushServerNode.Cache.Authentication;

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
