using HushServerNode.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HushServerNode.Cache.Authentication;

public static class CacheAuthenticationHostBuilder
{
    public static IHostBuilder RegisterCacheAuthenticationContext(this IHostBuilder builder)
    {
        return builder.ConfigureServices(services =>
        {
            services.AddDbContextFactory<CacheAuthenticationDbContext>();

            services.AddSingleton<IDbContextConfigurator, CacheAuthenticationDbContextConfigurator>();
            services.AddSingleton<CacheAuthenticationDbContextConfigurator>();


            // services.AddSingleton<IBlockchainCache, BlockchainCache>();
        });
    }
}
