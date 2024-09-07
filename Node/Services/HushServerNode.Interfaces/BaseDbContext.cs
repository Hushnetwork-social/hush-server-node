using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HushServerNode.Interfaces;

public class BaseDbContext : DbContext
{
    protected readonly IConfiguration _configuration;

    public BaseDbContext(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured) 
        {
            optionsBuilder.UseNpgsql($"Host={_configuration["DbSettings:Host"]}; Database={_configuration["DbSettings:Db"]}; Username={_configuration["DbSettings:User"]}; Password={_configuration["DbSettings:Password"]};");
        }
    }
}
