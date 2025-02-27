using HushNode.Interfaces;
using Microsoft.EntityFrameworkCore;

public class HushNodeDbContext : DbContext
{
    private readonly IEnumerable<IDbContextConfigurator> _dbContextConfigurators;

    public HushNodeDbContext(
        IEnumerable<IDbContextConfigurator> dbContextConfigurators,
        DbContextOptions<HushNodeDbContext> options) : base(options)
    {
        this._dbContextConfigurators = dbContextConfigurators;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach(var configurator in this._dbContextConfigurators)
        {
            configurator.Configure(modelBuilder);
        }
    }
}