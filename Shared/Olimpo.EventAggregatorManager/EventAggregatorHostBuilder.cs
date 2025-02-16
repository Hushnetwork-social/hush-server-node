using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Olimpo
{
    public static class EventAggregatorHostBuilder
    {
        public static IHostBuilder RegisterEventAggregatorManager(this IHostBuilder builder)
        {
            builder.ConfigureServices((hostContext, services) => 
            {
                services.RegisterEventAggregator();
            });

            return builder;
        }  
    }

    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection RegisterEventAggregator(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<IEventAggregator, EventAggregator>();

            return serviceCollection;
        }
    }
}