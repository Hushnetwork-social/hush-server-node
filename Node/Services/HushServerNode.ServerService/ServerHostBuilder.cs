using HushServerNode.ServerService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Olimpo;

namespace HushServerNode
{
    public static class ServerHostBuilder
    {
        public static IHostBuilder RegisterServerService(this IHostBuilder builder)
        {
            builder.ConfigureServices((hostContext, services) =>
            {
                services.AddSingleton<IBootstrapper, ServerBootstrapper>();

                services.AddSingleton<ITcpServerService, TcpServerService>();
            });

            return builder;
        }
    }
}