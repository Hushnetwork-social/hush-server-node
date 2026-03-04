using Microsoft.Extensions.DependencyInjection;
using HushNetwork.proto;
using HushNode.Interfaces;

namespace HushNode.Feeds.gRPC;

public static class HushNodeFeedsgRPCHostBuild
{
    public static void RegisterFeedsRPCServices(this IServiceCollection services)
    {
        services.AddSingleton<IGrpcDefinition, FeedsGrpcServiceDefinition>();
        services.AddSingleton<IInnerCircleApplicationService, InnerCircleApplicationService>();
        services.AddSingleton<IGroupMembershipApplicationService, GroupMembershipApplicationService>();
        services.AddSingleton<IGroupAdministrationApplicationService, GroupAdministrationApplicationService>();
        services.AddSingleton<HushFeed.HushFeedBase, FeedsGrpcService>();
    }
}
