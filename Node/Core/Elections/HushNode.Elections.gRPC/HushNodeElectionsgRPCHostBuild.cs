using HushNetwork.proto;
using HushNode.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace HushNode.Elections.gRPC;

public static class HushNodeElectionsgRPCHostBuild
{
    public static void RegisterElectionsRPCServices(this IServiceCollection services)
    {
        services.AddSingleton<IElectionQueryApplicationService, ElectionQueryApplicationService>();
        services.AddSingleton<IGrpcDefinition, ElectionsGrpcServiceDefinition>();
        services.AddSingleton<HushElections.HushElectionsBase, ElectionsGrpcService>();
    }
}
