using HushServerNode.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HushServerNode.Cache.Bank;

public static class CacheBankHostBuilder
{
    public static IHostBuilder RegisterCacheBankContext(this IHostBuilder builder)
    {
        return builder.ConfigureServices(services =>
        {
            services.AddDbContextFactory<CacheBankDbContext>();

            services.AddSingleton<IDbContextConfigurator, CacheBankDbContextConfigurator>();
            services.AddSingleton<CacheBankDbContextConfigurator>();


            // services.AddSingleton<IBlockchainCache, BlockchainCache>();
        });
    }
}
