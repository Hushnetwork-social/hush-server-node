using HushServerNode.Interfaces;
using HushServerNode.InternalModule.Feed.Cache;
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

        });

        return builder;
    }
}
