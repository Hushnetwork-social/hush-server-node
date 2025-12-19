using HushNode.Notifications.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HushNode.Notifications;

/// <summary>
/// Extension methods for registering notification services.
/// </summary>
public static class Registration
{
    /// <summary>
    /// Registers the notification and unread tracking services with Redis.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection RegisterNotificationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind Redis settings from configuration
        services.Configure<RedisSettings>(
            configuration.GetSection(RedisSettings.SectionName));

        // Register Redis connection manager as singleton
        services.AddSingleton<RedisConnectionManager>();

        // Register services
        services.AddSingleton<IUnreadTrackingService, UnreadTrackingService>();
        services.AddSingleton<INotificationService, NotificationService>();

        return services;
    }
}
