using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Olimpo;

namespace HushNetwork.BlockchainWorkflows;

public static class BlockchainWorkflowHostBuilder
{
    public static IHostBuilder RegisterBlockchainWorkflow(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) => 
        {
            services.AddSingleton<IBootstrapper, BlockchainWorkflowBootstrapper>();

            services.AddSingleton<IBlockchainWorkflow, BlockchainWorkflow>();
        });

        return builder;
    }
}
