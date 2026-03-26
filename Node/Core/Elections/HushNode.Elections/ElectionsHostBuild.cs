using HushNode.Elections.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HushNode.Elections;

public static class ElectionsHostBuild
{
    public static IHostBuilder RegisterCoreModuleElections(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) =>
        {
            services.RegisterElectionsStorageServices(hostContext);
            services.RegisterElectionsCoreServices();
        });

        return builder;
    }

    public static void RegisterElectionsCoreServices(this IServiceCollection services)
    {
        services.AddSingleton<IElectionLifecycleService, ElectionLifecycleService>();
    }
}
