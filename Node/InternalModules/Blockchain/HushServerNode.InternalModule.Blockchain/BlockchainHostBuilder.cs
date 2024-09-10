using HushServerNode.Cache.Blockchain;
using HushServerNode.Interfaces;
using HushServerNode.InternalModule.Blockchain.Builders;
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


            services.AddDbContextFactory<CacheBlockchainDbContext>();
            services.AddSingleton<IDbContextConfigurator, CacheBlockchainDbContextConfigurator>();
            services.AddSingleton<CacheBlockchainDbContextConfigurator>();


            // services.AddSingleton<IBlockchainDb, BlockchainDb>();
            // services.AddSingleton<IBlockchainIndexDb, BlockchainIndexDb>();

            // services.AddSingleton<IBootstrapper, BlockchainBootstrapper>();
            // services.AddSingleton<IBlockchainService, BlockchainService>();

            // services.AddSingleton<IBlockVerifier, BlockVerifier>();

            // services.AddSingleton<IMemPoolService, MemPoolService>();
            // services.AddSingleton<IBlockGeneratorService, BlockGeneratorService>();

            // services.AddTransient<IIndexStrategy, ValueableTransactionIndexStrategy>();
            // services.AddTransient<IIndexStrategy, GroupTransactionsByAddressIndexStrategy>();
            // services.AddTransient<IIndexStrategy, UserProfileIndexStrategy>();
            // services.AddTransient<IIndexStrategy, FeedIndexStrategy>();
            // services.AddTransient<IIndexStrategy, FeedMessageIndexStrategy>();
        });

        return builder;
    }
}
