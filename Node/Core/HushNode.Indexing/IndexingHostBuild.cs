using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Olimpo;

namespace HushNode.Indexing;

public static class IndexingHostBuild
{
    public static IHostBuilder RegisterHushNodeIndexing(this IHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IBootstrapper, IndexingBootstrapper>();
        });

        return builder;
    }
}
