using HushNode.Interfaces;
using HushNode.InternalModules.Identity.Model;
using Microsoft.EntityFrameworkCore;

namespace HushNode.InternalModules.Identity
{
    public class IdentityDbContextConfigurator : IDbContextConfigurator
    {
        public void Configure(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<Profile>()
                .ToTable("Profile", "Identity");

            modelBuilder
                .Entity<Profile>()
                .HasKey(x => x.PublicSigningAddress);
        }
    }
}
