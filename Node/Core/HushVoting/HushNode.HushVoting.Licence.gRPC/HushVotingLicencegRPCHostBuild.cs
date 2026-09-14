using HushNetwork.proto;
using HushNode.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace HushNode.HushVoting.Licence.gRPC;

/// <summary>
/// Host composition for the FEAT-015 licence query RPC surface. The dependency-safe query
/// application port is registered here; the host (HushServerNode) supplies the concrete
/// implementation in Phase 6.3/6.5 over identity storage, the Redis-first cache reader, the
/// indexed projection, and the current catalogue snapshot.
/// </summary>
public static class HushVotingLicencegRPCHostBuild
{
    public static IServiceCollection RegisterHushVotingLicenceQueryServices(
        this IServiceCollection services)
    {
        services.AddSingleton<IGrpcDefinition, HushVotingLicenceGrpcServiceDefinition>();
        services.AddSingleton<HushVotingLicence.HushVotingLicenceBase, HushVotingLicenceGrpcService>();
        return services;
    }
}
