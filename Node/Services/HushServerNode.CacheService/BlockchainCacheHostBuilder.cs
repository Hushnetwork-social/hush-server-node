using HushServerNode.CacheService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Olimpo;

namespace HushServerNode;

public static class BlockchainCacheHostBuilder
{
    public static IHostBuilder RegisterBlockchainCacheService(this IHostBuilder builder)
    {
        return builder.ConfigureServices(services =>
        {
            services.AddSingleton<IBootstrapper, BlockchainCacheBootstrapper>();

            services.AddDbContext<BlockchainDataContext>();

            services.AddSingleton<IBlockchainCache, BlockchainCache>();
        });
    }
}
