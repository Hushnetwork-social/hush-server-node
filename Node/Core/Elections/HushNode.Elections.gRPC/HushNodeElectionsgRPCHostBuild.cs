using HushNetwork.proto;
using HushNode.Caching;
using HushNode.Elections.Storage;
using HushNode.Interfaces;
using HushNode.MemPool;
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
                sp.GetRequiredService<ElectionCeremonyOptions>(),
                sp.GetRequiredService<IMemPoolService>(),
                sp.GetRequiredService<IElectionEnvelopeCryptoService>(),
                sp.GetRequiredService<IElectionCastIdempotencyCacheService>(),
                sp.GetRequiredService<IElectionBallotPublicationService>()));
        services.AddSingleton<IGrpcDefinition, ElectionsGrpcServiceDefinition>();
        services.AddSingleton<HushElections.HushElectionsBase, ElectionsGrpcService>();
    }
}
