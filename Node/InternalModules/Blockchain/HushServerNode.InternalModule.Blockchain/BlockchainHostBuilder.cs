using HushEcosystem.Model.Blockchain;
using HushServerNode.Interfaces;
using HushServerNode.InternalModule.Blockchain.Builders;
using HushServerNode.InternalModule.Blockchain.Cache;
using HushServerNode.InternalModule.Blockchain.Factories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Olimpo;

namespace HushServerNode.InternalModule.Blockchain;

public static class BlockchainHostBuilder
{
    public static IHostBuilder RegisterInternalModuleBlockchain(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) => 
        {
            services.AddTransient<IBlockBuilder, BlockBuilder>();
            services.AddTransient<IBlockchainDbAccess, BlockchainDbAccess>();
            services.AddSingleton<IBootstrapper, BlockchainBootstrapper>();

            services.AddSingleton<IBlockchainService, BlockchainService>();
            services.AddSingleton<IBlockGeneratorService, BlockGeneratorService>();

            services.AddSingleton<IBlockCreatedEventFactory, BlockCreatedEventFactory>();

            services.AddSingleton<IBlockVerifier, BlockVerifier>();

            services.AddSingleton<IBlockchainStatus, BlockchainStatus>();

            // Register Blockchain Cache 
            services.AddDbContextFactory<CacheBlockchainDbContext>();
            services.AddSingleton<IDbContextConfigurator, CacheBlockchainDbContextConfigurator>();
            services.AddSingleton<CacheBlockchainDbContextConfigurator>();
        });

        return builder;
    }
}
