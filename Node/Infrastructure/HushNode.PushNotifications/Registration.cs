using System.Net;
using HushNode.Interfaces;
using HushNode.PushNotifications.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.PushNotifications;

/// <summary>
/// Extension methods for registering push notification services.
/// </summary>
public static class Registration
{
    /// <summary>
    /// Registers push notification module services using IHostBuilder pattern.
    /// </summary>
    public static IHostBuilder RegisterPushNotificationsModule(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) =>
        {
            services.RegisterPushNotificationsStorageServices(hostContext);
        });

        return builder;
    }

    /// <summary>
    /// Registers push notification storage services and DbContext configurator.
    /// </summary>
    public static void RegisterPushNotificationsStorageServices(this IServiceCollection services, HostBuilderContext hostContext)
    {
        // Register DbContext with PostgreSQL connection
        services.AddDbContext<PushNotificationsDbContext>((provider, options) =>
        {
            options.UseNpgsql(hostContext.Configuration.GetConnectionString("HushNetworkDb"));
            options.EnableSensitiveDataLogging();
            options.EnableDetailedErrors();
        });

        // Register Unit of Work provider
        services.AddTransient<IUnitOfWorkProvider<PushNotificationsDbContext>, UnitOfWorkProvider<PushNotificationsDbContext>>();

        // Register the configurator for HushNodeDbContext (main DbContext)
        services.AddTransient<IDbContextConfigurator, PushNotificationsDbContextConfigurator>();
        services.AddTransient<PushNotificationsDbContextConfigurator>();

        // Register repository
        services.AddTransient<IDeviceTokenRepository, DeviceTokenRepository>();

        // Register storage service
        services.AddTransient<IDeviceTokenStorageService, DeviceTokenStorageService>();

        // Register FCM provider (singleton - Firebase SDK is thread-safe)
        services.AddSingleton<IFcmProvider, FcmProvider>();

        // Register APNs provider (singleton - holds ECDsa key + JWT cache)
        services.Configure<ApnsSettings>(hostContext.Configuration.GetSection("ApnsSettings"));
        services.AddHttpClient("ApnsClient")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true
            });
        services.AddSingleton<IApnsProvider, ApnsProvider>();

        // Register push delivery service (transient - stateless service)
        services.AddTransient<IPushDeliveryService, PushDeliveryService>();

        // Register background cleanup service
        services.AddHostedService<TokenCleanupService>();
    }
}
