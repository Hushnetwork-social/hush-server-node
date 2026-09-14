using HushNode.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HushNode.HushVoting.Licensing.Storage;

/// <summary>
/// Host composition entry point for the HushVoting licensing storage module.
/// Phase 2 registers the unified-model configurator; later phases add repositories,
/// the internal entitlement service, readiness, clock, and telemetry here.
/// </summary>
public static class LicensingStorageHostBuild
{
    public static IHostBuilder RegisterHushVotingLicensing(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) =>
        {
            services.RegisterHushVotingLicensingStorageServices(hostContext);
        });

        return builder;
    }

    public static void RegisterHushVotingLicensingStorageServices(
        this IServiceCollection services,
        HostBuilderContext hostContext)
    {
        // Unified HushNodeDbContext contribution (single configurator; one migration stream).
        services.AddTransient<IDbContextConfigurator, LicensingDbContextConfigurator>();
        services.AddTransient<LicensingDbContextConfigurator>();
    }
}
