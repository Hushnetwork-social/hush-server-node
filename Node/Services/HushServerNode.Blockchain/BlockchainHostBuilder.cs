using HushServerNode.Blockchain;
using HushServerNode.Blockchain.Builders;
using HushServerNode.Blockchain.Factories;
using HushServerNode.Blockchain.IndexStrategies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Olimpo;

namespace HushServerNode;

public static class BlockchainHostBuilder
{
    public static IHostBuilder RegisterBlockchainService(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) => 
        {
            services.AddTransient<IBlockBuilder, BlockBuilder>();

            services.AddSingleton<IBlockchainDb, BlockchainDb>();
            services.AddSingleton<IBlockchainIndexDb, BlockchainIndexDb>();                 // TODO [AboimPinto] this is a local database but in the future need to be a presistent database.

            services.AddSingleton<IBootstrapper, BlockchainBootstrapper>();
            services.AddSingleton<IBlockchainService, BlockchainService>();

            services.AddSingleton<IBlockVerifier, BlockVerifier>();

            services.AddSingleton<IMemPoolService, MemPoolService>();
            services.AddSingleton<IBlockGeneratorService, BlockGeneratorService>();

            services.AddSingleton<IBlockCreatedEventFactory, BlockCreatedEventFactory>();

            services.AddTransient<IIndexStrategy, ValueableTransactionIndexStrategy>();
            services.AddTransient<IIndexStrategy, GroupTransactionsByAddressIndexStrategy>();
            services.AddTransient<IIndexStrategy, UserProfileIndexStrategy>();
            services.AddTransient<IIndexStrategy, FeedIndexStrategy>();
            services.AddTransient<IIndexStrategy, FeedMessageIndexStrategy>();
        });

        return builder;
    }
}
