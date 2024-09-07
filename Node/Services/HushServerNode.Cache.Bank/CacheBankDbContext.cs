using HushServerNode.Interfaces;
using Microsoft.Extensions.Configuration;

namespace HushServerNode.Cache.Bank;

public class CacheBankDbContext : BaseDbContext
{
    public CacheBankDbContext(IConfiguration configuration) : base(configuration)
    {
    }
}
