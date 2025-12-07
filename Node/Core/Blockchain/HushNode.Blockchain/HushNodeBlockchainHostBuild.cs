using HushNode.Blockchain.Configuration;
using HushNode.Blockchain.gRPC;
using HushNode.Blockchain.Services;
using HushNode.Blockchain.Storage;
using HushNode.Blockchain.Workflows;
using HushShared.Blockchain.TransactionModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Olimpo;

namespace HushNode.Blockchain;

public static class HushNodeBlockchainHostBuild
{
    public static IHostBuilder RegisterCoreModuleBlockchain(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) =>
        {
            services.Configure<BlockchainSettings>(
                hostContext.Configuration.GetSection("BlockchainSettings"));

            services.AddSingleton<IBootstrapper, HushNodeBlockchainBootstrapper>();

            services.AddSingleton<IChainFoundationService, ChainFoundationService>();
            services.AddSingleton<IBlockProductionSchedulerService, BlockProductionSchedulerService>();
            services.AddSingleton<IBlockAssemblerWorkflow, BlockAssemblerWorkflow>();

            services.RegisterBlockchainStorageServices(hostContext);
            services.RegisterBlockchaingRPCServices();

            services.AddSingleton<TransactionDeserializerHandler>();

            // builder.RegisterBlockchainStorage();
            // builder.RegisterHushNodeBlockchaingRPC();
        });

        return builder;
    }
}
