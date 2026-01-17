using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HushNode.UrlMetadata.gRPC;

public static class HushNodeUrlMetadataHostBuild
{
    /// <summary>
    /// Registers all URL metadata services including gRPC handler.
    /// </summary>
    public static IHostBuilder RegisterCoreModuleUrlMetadata(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) =>
        {
            // Register business logic services
            services.AddSingleton<IUrlBlocklist, UrlBlocklist>();
            services.AddSingleton<IUrlMetadataCacheService, UrlMetadataCacheService>();
            services.AddTransient<IOpenGraphParser, OpenGraphParser>();
            services.AddTransient<IImageProcessor, ImageProcessor>();

            // Register HttpClient for OpenGraphParser
            services.AddHttpClient<IOpenGraphParser, OpenGraphParser>();
            services.AddHttpClient<IImageProcessor, ImageProcessor>();

            // Register gRPC service
            services.AddScoped<UrlMetadataGrpcService>();
        });

        return builder;
    }
}
