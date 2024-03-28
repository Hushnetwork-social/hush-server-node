using HushNetwork.proto;
using HushServerNode.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HushServerNode;

public static class RegisterGrpcServer
{
    public static IHostBuilder RegisterGrpc(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<HushProfile.HushProfileBase, HushProfileService>();
            services.AddSingleton<HushBlockchain.HushBlockchainBase, HushBlockchainService>();
        });

        return builder;
    }
}
