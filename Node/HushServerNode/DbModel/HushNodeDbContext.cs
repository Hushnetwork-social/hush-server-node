using HushServerNode.Interfaces;
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
