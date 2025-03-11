using Microsoft.Extensions.DependencyInjection;
using HushNetwork.proto;
using HushNode.Interfaces;

namespace HushNode.Blockchain.gRPC;

public static class HushNodeBlockchainhRPCHostBuild
{
    public static void RegisterBlockchaingRPCServices(this IServiceCollection services)
    {
        services.AddSingleton<IGrpcDefinition, BlockchainGrpcServiceDefinition>();
        services.AddSingleton<HushBlockchain.HushBlockchainBase, BlockchainGrpcService>();
    }
}
