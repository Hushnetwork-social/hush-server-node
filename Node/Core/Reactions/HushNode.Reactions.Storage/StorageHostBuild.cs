using HushNode.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Reactions.Storage;

public static class StorageHostBuild
{
    public static void RegisterReactionsStorageServices(this IServiceCollection services, HostBuilderContext hostContext)
    {
        services.AddDbContext<ReactionsDbContext>((provider, options) =>
        {
            options.UseNpgsql(hostContext.Configuration.GetConnectionString("HushNetworkDb"));
            options.EnableSensitiveDataLogging();
            options.EnableDetailedErrors();
        });

        services.AddTransient<IUnitOfWorkProvider<ReactionsDbContext>, UnitOfWorkProvider<ReactionsDbContext>>();

        services.AddTransient<IDbContextConfigurator, ReactionsDbContextConfigurator>();
        services.AddTransient<ReactionsDbContextConfigurator>();

        services.AddTransient<IReactionsRepository, ReactionsRepository>();
        services.AddTransient<IMerkleTreeRepository, MerkleTreeRepository>();
        services.AddTransient<ICommitmentRepository, CommitmentRepository>();
    }
}
