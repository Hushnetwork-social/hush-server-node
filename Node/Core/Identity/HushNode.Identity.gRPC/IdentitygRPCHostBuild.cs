using HushNetwork.proto;
using HushNode.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace HushNode.Identity.gRPC;

public static class IdentitygRPCHostBuild
{
    public static void RegisterIdentitygRPCServices(this IServiceCollection services)
    {
        services.AddSingleton<IGrpcDefinition, IdentityGrpcServiceDefinition>();
        services.AddSingleton<HushProfile.HushProfileBase, IdentityGrpcService>();
    }
}
