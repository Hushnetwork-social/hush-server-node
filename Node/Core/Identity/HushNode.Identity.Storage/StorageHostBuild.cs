using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using HushNode.Interfaces;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Identity.Storage;

public static class StorageHostBuild
{
    public static void RegisterIdentityStorageServices(this IServiceCollection services, HostBuilderContext hostContext)
    {
        services.AddDbContext<IdentityDbContext>((provider, options) => 
        {
            options.UseNpgsql(hostContext.Configuration.GetConnectionString("HushNetworkDb"));
            options.EnableSensitiveDataLogging();  // For debugging
            options.EnableDetailedErrors();  // For debugging
        });

        services.AddTransient<IUnitOfWorkProvider<IdentityDbContext>, UnitOfWorkProvider<IdentityDbContext>>();

        services.AddSingleton<IIdentityStorageService, IdentityStorageService>();
        services.AddTransient<IIdentityRepository, IdentityRepository>();

        services.AddTransient<IDbContextConfigurator, IdentityDbContextConfigurator>();
        services.AddTransient<IdentityDbContextConfigurator>();
    }
}
