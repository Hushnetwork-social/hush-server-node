using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Olimpo.EntityFramework.Persistency;
using HushNode.Interfaces;


namespace HushNode.Blockchain.Storage;

public static class StorageHostBuild
{
    public static void RegisterBlockchainStorageServices(this IServiceCollection services, HostBuilderContext hostContext)
    {
        services.AddDbContext<BlockchainDbContext>((provider, options) => 
        {
            options.UseNpgsql(hostContext.Configuration.GetConnectionString("HushNetworkDb"));
            options.ConfigureWarnings(warnings =>
            {
                warnings.Ignore(RelationalEventId.CommandExecuting);
                warnings.Ignore(RelationalEventId.CommandExecuted);
            });
            options.EnableDetailedErrors();  // For debugging
        });

        services.AddTransient<IUnitOfWorkProvider<BlockchainDbContext>, UnitOfWorkProvider<BlockchainDbContext>>();

        services.AddTransient<IBlockchainStorageService, BlockchainStorageService>();

        services.AddTransient<IBlockRepository, BlockRepository>();
        services.AddTransient<IBlockchainStateRepository, BlockchainStateRepository>();

        services.AddTransient<IDbContextConfigurator, BlockchainDbContextConfigurator>();
        services.AddTransient<BlockchainDbContextConfigurator>();
    }
}
