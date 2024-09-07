using HushServerNode.Interfaces;
using Microsoft.Extensions.Configuration;

namespace HushServerNode.Cache.Feed;

public class CacheFeedDbContext : BaseDbContext
{
    public CacheFeedDbContext(IConfiguration configuration) : base(configuration)
    {
    }
}
