using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

namespace HushNode.Notifications.gRPC;

/// <summary>
/// Extension methods for registering notification gRPC services.
/// </summary>
public static class Registration
{
    /// <summary>
    /// Registers the notification gRPC service and its dependencies.
    /// </summary>
    /// <param name="builder">The host builder.</param>
    /// <returns>The host builder for chaining.</returns>
    public static IHostBuilder RegisterNotificationGrpc(this IHostBuilder builder)
    {
        builder.ConfigureServices((context, services) =>
        {
            // Register the notification services from the base project
            services.RegisterNotificationServices(context.Configuration);

            // Register the gRPC service
            services.AddScoped<NotificationGrpcService>();
        });

        return builder;
    }
}
