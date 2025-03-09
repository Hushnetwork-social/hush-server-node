using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Olimpo.EntityFramework.Persistency;
using HushNode.Interfaces;


namespace HushNode.Blockchain.Repositories;

public static class RepositoriesHostBuild
{
    public static IHostBuilder RegisterBlockchainRepositories(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) => 
        {
            services.AddDbContext<BlockchainDbContext>((provider, options) => 
            {
                options.UseNpgsql(hostContext.Configuration.GetConnectionString("HushNetworkDb"));
                options.EnableSensitiveDataLogging();  // For debugging
                options.EnableDetailedErrors();  // For debugging
            });

            services.AddTransient<IUnitOfWorkProvider<BlockchainDbContext>, UnitOfWorkProvider<BlockchainDbContext>>();

            services.AddTransient<IBlockRepository, BlockRepository>();
            services.AddTransient<IBlockchainStateRepository, BlockchainStateRepository>();

            services.AddTransient<IDbContextConfigurator, BlockchainDbContextConfigurator>();
            services.AddTransient<BlockchainDbContextConfigurator>();
        });

        return builder;
    }
}
