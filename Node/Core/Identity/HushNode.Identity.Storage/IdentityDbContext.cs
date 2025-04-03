using HushShared.Identity.Model;
using Microsoft.EntityFrameworkCore;

namespace HushNode.Identity.Storage
{
    public class IdentityDbContext(
        IdentityDbContextConfigurator identityDbContextConfigurator, 
        DbContextOptions<IdentityDbContext> options) : DbContext(options)
    {
        private readonly IdentityDbContextConfigurator _identityDbContextConfigurator = identityDbContextConfigurator;

        public DbSet<Profile> Profiles { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            this._identityDbContextConfigurator.Configure(modelBuilder);
        }
    }
}
