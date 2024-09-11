using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Olimpo;

namespace HushServerNode.InternalModule.MemPool;

public static class MemPoolHostBuilder
{
    public static IHostBuilder RegisterInternalModuleMemPool(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) => 
        {
            services.AddSingleton<IBootstrapper, MemPoolBootstrapper>();

            services.AddSingleton<IMemPoolService, MemPoolService>();
        });

        return builder;
    }
}
