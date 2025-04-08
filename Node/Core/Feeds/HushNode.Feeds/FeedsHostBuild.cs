using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Olimpo;
using HushNode.Feeds.Storage;
using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;

namespace HushNode.Feeds;

public static class FeedsHostBuild
{
    public static IHostBuilder RegisterCoreModuleFeeds(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IBootstrapper, FeedsBootstrapper>();

            services.AddSingleton<IFeedsInitializationWorkflow, FeedsInitializationWorkflow>();
            services.AddSingleton<INewFeedTransactionHandler, NewFeedTransactionHandler>();

            services.RegisterFeedsStorageServices(hostContext);

            services.AddTransient<ITransactionDeserializerStrategy, NewPersonalFeedDeserializerStrategy>();
            services.AddTransient<IIndexStrategy, NewPersonalFeedIndexStrategy>(); 
        });

        return builder;
    }
}
