using Microsoft.EntityFrameworkCore;
using HushNode.Interfaces;
using HushShared.Identity.Model;

namespace HushNode.Identity.Storage
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
