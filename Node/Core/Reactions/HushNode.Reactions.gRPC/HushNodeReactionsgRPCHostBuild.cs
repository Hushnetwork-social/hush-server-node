using Microsoft.Extensions.DependencyInjection;
using HushNetwork.proto;
using HushNode.Interfaces;

namespace HushNode.Reactions.gRPC;

public static class HushNodeReactionsgRPCHostBuild
{
    public static void RegisterReactionsRPCServices(this IServiceCollection services)
    {
        services.AddSingleton<IGrpcDefinition, ReactionsGrpcServiceDefinition>();
        services.AddSingleton<HushReactions.HushReactionsBase, ReactionsGrpcService>();

        services.AddSingleton<IGrpcDefinition, MembershipGrpcServiceDefinition>();
        services.AddSingleton<HushMembership.HushMembershipBase, MembershipGrpcService>();
    }
}
