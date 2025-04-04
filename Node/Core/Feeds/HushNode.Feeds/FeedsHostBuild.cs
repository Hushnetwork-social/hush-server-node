using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Olimpo;

namespace HushNode.Feeds;

public static class FeedsHostBuild
{
    public static IHostBuilder RegisterCoreModuleFeeds(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IBootstrapper, FeedsBootstrapper>();
        });

        return builder;
    }
}
