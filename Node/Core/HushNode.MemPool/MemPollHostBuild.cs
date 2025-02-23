using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Olimpo;

namespace HushNode.MemPool;

public static class MemPollHostBuild
{
    public static IHostBuilder RegisterHushNodeMemPool(this IHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IBootstrapper, MemPoolBoopstrapper>();

            services.AddSingleton<IMemPoolService, MemPoolService>();
        });

        return builder;
    }
}
