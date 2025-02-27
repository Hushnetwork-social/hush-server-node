using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using HushNode.Blockchain.Persistency.Abstractions;
using HushNode.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using HushNode.Blockchain.Persistency.Abstractions.Repositories;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Blockchain.Persistency.EntityFramework;

public static class BlockchainPersistencyEntityFrameworkHostBuild
{
    public static IHostBuilder RegisterEntityFrameworkPersistency(this IHostBuilder builder)
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
