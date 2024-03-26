using HushServerNode.RpcManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Olimpo;

namespace HushServerNode;

public static class RpcHostBuilder
{
    public static IHostBuilder RegisterRpcManager(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IBootstrapper, RpcBootstrapper>();

            services.AddSingleton<IRpc, Rpc>();
        });

        return builder;
    }
}
