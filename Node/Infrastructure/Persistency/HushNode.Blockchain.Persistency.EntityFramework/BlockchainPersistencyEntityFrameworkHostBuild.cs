using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using HushNode.Blockchain.Persistency.Abstractions;
using HushNode.Interfaces;

namespace HushNode.Blockchain.Persistency.EntityFramework;

public static class BlockchainPersistencyEntityFrameworkHostBuild
{
    public static IHostBuilder RegisterEntityFrameworkPersistency(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) => 
        {
            services.AddDbContextFactory<BlockchainDbContext>();
            services.AddSingleton<IDbContextConfigurator, BlockchainDbContextConfigurator>();
            services.AddSingleton<BlockchainDbContextConfigurator>();

            services.AddSingleton<IUnitOfWorkFactory, UnitOfWorkFactory>();
        });

        return builder;
    }
}
