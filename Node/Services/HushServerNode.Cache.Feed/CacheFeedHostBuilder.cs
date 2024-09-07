using HushServerNode.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HushServerNode.Cache.Feed;

public static class CacheFeedHostBuilder
{
    public static IHostBuilder RegisterCacheFeedContext(this IHostBuilder builder)
    {
        return builder.ConfigureServices(services =>
        {
            services.AddDbContextFactory<CacheFeedDbContext>();

            services.AddSingleton<IDbContextConfigurator, CacheFeedDbContextConfigurator>();
            services.AddSingleton<CacheFeedDbContextConfigurator>();


            // services.AddSingleton<IBlockchainCache, BlockchainCache>();
        });
    }
}
