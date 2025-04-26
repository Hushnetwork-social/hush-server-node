using HushNetwork.proto;
using HushNode.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace HushNode.Bank.gRPC;

public static class HushNodeBankgRPCHostBuild
{
    public static void RegisterBankRPCServices(this IServiceCollection services)
    {
        services.AddSingleton<IGrpcDefinition, BankGrpcServiceDefinition>();
        services.AddSingleton<HushBank.HushBankBase, BankGrpService>();
    }
}
