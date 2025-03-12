using HushNode.Identity.Model;
using HushNode.Interfaces;
using Microsoft.EntityFrameworkCore;

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
