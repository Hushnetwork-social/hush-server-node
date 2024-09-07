using HushServerNode.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HushServerNode.Cache.Blockchain;

public static class CacheBlockchainHostBuilder
{
    public static IHostBuilder RegisterCacheBlockchainContext(this IHostBuilder builder)
    {
        return builder.ConfigureServices(services =>
        {
            services.AddDbContextFactory<CacheABlockchainDbContext>();

            services.AddSingleton<IDbContextConfigurator, CacheBlockchainDbContextConfigurator>();
            services.AddSingleton<CacheBlockchainDbContextConfigurator>();


            // services.AddSingleton<IBlockchainCache, BlockchainCache>();
        });
    }
}