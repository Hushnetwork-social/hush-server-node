using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HushNode.Idempotency;

/// <summary>
/// FEAT-057: Extension method for registering IdempotencyService in dependency injection.
/// IdempotencyService is registered as Singleton to maintain MemPool tracking state across requests.
/// </summary>
public static class IdempotencyHostBuild
{
    /// <summary>
    /// Registers the Idempotency module services.
    /// Must be called AFTER RegisterCoreModuleFeeds (depends on IFeedMessageRepository).
    /// Must be called BEFORE RegisterHushNodeMemPool (MemPoolService depends on IIdempotencyService).
    /// </summary>
    public static IHostBuilder RegisterIdempotencyModule(this IHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // FEAT-057: Register as Singleton - MemPool tracking must persist across requests
            services.AddSingleton<IIdempotencyService, IdempotencyService>();
        });

        return builder;
    }
}
