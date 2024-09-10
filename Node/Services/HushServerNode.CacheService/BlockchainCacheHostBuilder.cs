using HushServerNode.CacheService;
using HushServerNode.Interfaces;
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
            // services.AddSingleton<IBootstrapper, BlockchainCacheBootstrapper>();

            // // services.AddDbContext<BlockchainDataContext>();
            // services.AddDbContextFactory<BlockchainDataContext>();

            // services.AddSingleton<IDbContextConfigurator, CacheServiceConfigurator>();
            // services.AddSingleton<CacheServiceConfigurator>();


            // services.AddSingleton<IBlockchainCache, BlockchainCache>();
        });
    }
}
