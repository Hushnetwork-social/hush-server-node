using Microsoft.EntityFrameworkCore;
using HushNode.Interfaces;
using HushShared.Identity.Model;
using HushShared.Blockchain.BlockModel;

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

            modelBuilder
                .Entity<Profile>()
                .Property(x => x.BlockIndex)
                .HasConversion(
                    x => x.Value,
                    x => new BlockIndex(x)
                )
                .HasColumnType("bigint");
        }
    }
}
