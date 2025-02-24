using HushNode.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HushServerNode.DbModel;

public class HushNodeDbContext(IEnumerable<IDbContextConfigurator> dbContextConfigurators) : DbContext
{
    private readonly IEnumerable<IDbContextConfigurator> _dbContextConfigurators = dbContextConfigurators;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach (var dbContextConfigurator in this._dbContextConfigurators)
        {
            dbContextConfigurator.Configure(modelBuilder);    
        }
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseNpgsql("Host=localhost;Database=HushNetworkDb;Username=HushNetworkDb_USER;Password=HushNetworkDb_PASSWORD");
        }
    }
}

// public class BaseDbContext : DbContext
// {
//     protected readonly IConfiguration _configuration;

//     public BaseDbContext(IConfiguration configuration)
//     {
//         this._configuration = configuration;
//     }

//     protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
//     {
//         if (!optionsBuilder.IsConfigured) 
//         {
//             optionsBuilder.UseNpgsql($"Host={_configuration["DbSettings:Host"]}; Database={_configuration["DbSettings:Db"]}; Username={_configuration["DbSettings:User"]}; Password={_configuration["DbSettings:Password"]};");
//         }
//     }
// }