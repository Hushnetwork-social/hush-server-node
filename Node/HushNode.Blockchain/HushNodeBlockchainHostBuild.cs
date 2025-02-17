using HushNode.Blockchain.Services;
using HushNode.Blockchain.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Olimpo;

namespace HushNode.Blockchain;

public static class HushNodeBlockchainHostBuild
{
    public static IHostBuilder RegisterHushNodeBlockchain(this IHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IChainFoundationService, ChainFoundationService>();
            services.AddSingleton<IBlockProductionSchedulerService, BlockProductionSchedulerService>();
            services.AddSingleton<IBlockAssemblerWorkflow, BlockAssemblerWorkflow>();

            services.AddSingleton<IBootstrapper, HushNodeBlockchainBootstrapper>();
        });

        return builder;
    }
}
