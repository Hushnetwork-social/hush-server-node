using HushNode.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HushServerNode.DbModel;

public class HushNodeDbContext : BaseDbContext
{
    private readonly IEnumerable<IDbContextConfigurator> _dbContextConfigurators;

    public HushNodeDbContext(
        IEnumerable<IDbContextConfigurator> dbContextConfigurators,
        IConfiguration configuration) : base(configuration)
    {
        this._dbContextConfigurators = dbContextConfigurators;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach (var dbContextConfigurator in this._dbContextConfigurators)
        {
            dbContextConfigurator.Configure(modelBuilder);    
        }
    }
}

public class BaseDbContext : DbContext
{
    protected readonly IConfiguration _configuration;

    public BaseDbContext(IConfiguration configuration)
    {
        this._configuration = configuration;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured) 
        {
            optionsBuilder.UseNpgsql($"Host={_configuration["DbSettings:Host"]}; Database={_configuration["DbSettings:Db"]}; Username={_configuration["DbSettings:User"]}; Password={_configuration["DbSettings:Password"]};");
        }
    }
}