using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Olimpo;

public static class BootstrapperHostBuilder
{
    public static IHostBuilder RegisterBootstrapperManager(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) => 
        {
            services.RegisterBootstrapper();
        });

        return builder;
    }  
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection RegisterBootstrapper(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<IBootstrapperManager, BootstrapperManager>();

        return serviceCollection;
    }
}
