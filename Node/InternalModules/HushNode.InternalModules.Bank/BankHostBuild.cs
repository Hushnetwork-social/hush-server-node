using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Olimpo.EntityFramework.Persistency;
using HushNode.Indexing;
using HushNode.Interfaces;

namespace HushNode.InternalModules.Bank;

public static class BankHostBuild
{
    public static IHostBuilder RegisterInternalModuleBank(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IIndexStrategy, RewardTransactionIndexStrategy>();

            services.AddSingleton<IRewardTransactionHandler, RewardTransactionHandler>();

            services.AddDbContext<BankDbContext>((provider, options) => 
            {
                options.UseNpgsql(hostContext.Configuration.GetConnectionString("HushNetworkDb"));
                options.EnableSensitiveDataLogging();  // For debugging
                options.EnableDetailedErrors();  // For debugging
            });

            services.AddTransient<IUnitOfWorkProvider<BankDbContext>, UnitOfWorkProvider<BankDbContext>>();

            services.AddTransient<IBalanceRepository, BalanceRepository>();

            services.AddTransient<IDbContextConfigurator, BankDbContextConfigurator>();
            services.AddTransient<BankDbContextConfigurator>();
        });

        return builder;
    }
}
