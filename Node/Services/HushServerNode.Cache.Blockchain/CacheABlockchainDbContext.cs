using HushServerNode.Interfaces;
using Microsoft.Extensions.Configuration;

namespace HushServerNode.Cache.Blockchain;

public class CacheABlockchainDbContext : BaseDbContext
{
    public CacheABlockchainDbContext(IConfiguration configuration) : base(configuration)
    {
    }
}
