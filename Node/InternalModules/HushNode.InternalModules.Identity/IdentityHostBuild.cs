using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Olimpo.EntityFramework.Persistency;
using HushNode.Interfaces;
using HushNetwork.proto;

namespace HushNode.InternalModules.Identity;

public static class IdentityHostBuild
{
    public static IHostBuilder RegisterInternalModuleIdentity(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) =>
        {
            // services.AddSingleton<IIndexStrategy, UserProfileTransactionIndexStrategy>();

            // services.AddSingleton<IRewardTransactionHandler, RewardTransactionHandler>();

            services.AddDbContext<IdentityDbContext>((provider, options) => 
            {
                options.UseNpgsql(hostContext.Configuration.GetConnectionString("HushNetworkDb"));
                options.EnableSensitiveDataLogging();  // For debugging
                options.EnableDetailedErrors();  // For debugging
            });

            services.AddTransient<IUnitOfWorkProvider<IdentityDbContext>, UnitOfWorkProvider<IdentityDbContext>>();

            services.AddTransient<IIdentityRepository, IdentityRepository>();

            services.AddTransient<IDbContextConfigurator, IdentityDbContextConfigurator>();
            services.AddTransient<IdentityDbContextConfigurator>();

            services.AddSingleton<IGrpcDefinition, IdentityGrpcServiceDefinition>();
            services.AddSingleton<HushProfile.HushProfileBase, IdentityGrpcService>();
        });

        return builder;
    }
}
