using HushNode.InternalModules.Identity.Model;
using Microsoft.EntityFrameworkCore;

namespace HushNode.InternalModules.Identity;

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
