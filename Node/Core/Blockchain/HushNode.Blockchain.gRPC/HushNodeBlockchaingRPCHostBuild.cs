using HushNetwork.proto;
using HushNode.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HushNode.Blockchain.gRPC;

public static class HushNodeBlockchainhRPCHostBuild
{
    public static IHostBuilder RegisterHushNodeBlockchaingRPC(this IHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IGrpcDefinition, BlockchainGrpcServiceDefinition>();
            services.AddSingleton<HushBlockchain.HushBlockchainBase, BlockchainGrpcService>();
        });

        return builder;
    }
}
