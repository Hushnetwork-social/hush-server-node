using HushNode.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections.Storage;

public static class StorageHostBuild
{
    public static void RegisterElectionsStorageServices(this IServiceCollection services, HostBuilderContext hostContext)
    {
        services.AddDbContext<ElectionsDbContext>((provider, options) =>
        {
            options.UseNpgsql(hostContext.Configuration.GetConnectionString("HushNetworkDb"));
            options.ConfigureWarnings(warnings =>
            {
                warnings.Ignore(RelationalEventId.CommandExecuting);
                warnings.Ignore(RelationalEventId.CommandExecuted);
            });
            options.EnableDetailedErrors();
        });

        services.AddTransient<IUnitOfWorkProvider<ElectionsDbContext>, UnitOfWorkProvider<ElectionsDbContext>>();

        services.AddTransient<IDbContextConfigurator, ElectionsDbContextConfigurator>();
        services.AddTransient<ElectionsDbContextConfigurator>();

        services.AddTransient<IElectionsRepository, ElectionsRepository>();
    }
}
