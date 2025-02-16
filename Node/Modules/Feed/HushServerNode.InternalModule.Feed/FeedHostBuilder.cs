using HushNetwork.proto;
using HushServerNode.Blockchain.IndexStrategies;
using HushServerNode.Interfaces;
using HushServerNode.InternalModule.Feed.Cache;
using HushServerNode.InternalModule.Feed.IndexStrategies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HushServerNode.InternalModule.Feed;

public static class FeedHostBuilder
{
    public static IHostBuilder RegisterInternalModuleFeed(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) => 
        {
            services.AddTransient<IFeedService, FeedService>();
            services.AddTransient<IFeedDbAccess, FeedDbAccess>();

            // Register Feed Cache 
            services.AddDbContextFactory<CacheFeedDbContext>();
            services.AddSingleton<IDbContextConfigurator, CacheFeedDbContextConfigurator>();
            services.AddSingleton<CacheFeedDbContextConfigurator>();

            // IndexStrategies
            services.AddTransient<IIndexStrategy, FeedIndexStrategy>();
            services.AddTransient<IIndexStrategy, FeedMessageIndexStrategy>();

            services.AddSingleton<IGrpcDefinition, FeedGrpcServiceDefinition>();
            services.AddSingleton<HushFeed.HushFeedBase, FeedGrpcService>();
        });

        return builder;
    }
}
