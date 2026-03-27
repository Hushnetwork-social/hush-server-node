using HushNode.Elections.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Olimpo;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections;

public static class ElectionsHostBuild
{
    public static IHostBuilder RegisterCoreModuleElections(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton(CreateCeremonyOptions(hostContext.Configuration));
            services.AddSingleton<IBootstrapper, ElectionCeremonyProfileRegistryBootstrapper>();
            services.RegisterElectionsStorageServices(hostContext);
            services.RegisterElectionsCoreServices();
        });

        return builder;
    }

    public static void RegisterElectionsCoreServices(this IServiceCollection services)
    {
        services.AddSingleton<IElectionLifecycleService>(sp =>
            new ElectionLifecycleService(
                sp.GetRequiredService<IUnitOfWorkProvider<ElectionsDbContext>>(),
                sp.GetRequiredService<ILogger<ElectionLifecycleService>>(),
                sp.GetRequiredService<ElectionCeremonyOptions>()));
    }

    private static ElectionCeremonyOptions CreateCeremonyOptions(IConfiguration configuration) =>
        new(
            EnableDevCeremonyProfiles: configuration.GetValue(
                "Elections:Ceremony:EnableDevCeremonyProfiles",
                defaultValue: true),
            ApprovedRegistryRelativePath: configuration.GetValue(
                "Elections:Ceremony:ApprovedRegistryRelativePath",
                defaultValue: ElectionCeremonyProfileCatalog.GetDefaultRegistryRelativePath())!,
            RequiredRolloutVersion: configuration.GetValue(
                "Elections:Ceremony:RequiredRolloutVersion",
                defaultValue: ElectionCeremonyProfileCatalog.ExpectedVersion)!);
}
