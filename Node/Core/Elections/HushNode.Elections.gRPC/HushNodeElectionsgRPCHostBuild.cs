using HushNetwork.proto;
using HushNode.Elections.Storage;
using HushNode.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections.gRPC;

public static class HushNodeElectionsgRPCHostBuild
{
    public static void RegisterElectionsRPCServices(this IServiceCollection services)
    {
        services.AddSingleton<IElectionQueryApplicationService>(sp =>
            new ElectionQueryApplicationService(
                sp.GetRequiredService<IUnitOfWorkProvider<ElectionsDbContext>>(),
                sp.GetRequiredService<ElectionCeremonyOptions>()));
        services.AddSingleton<IGrpcDefinition, ElectionsGrpcServiceDefinition>();
        services.AddSingleton<HushElections.HushElectionsBase, ElectionsGrpcService>();
    }
}
