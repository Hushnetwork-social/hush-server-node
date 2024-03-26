using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Olimpo;
using HushEcosystem;

namespace HushServerNode;

public class Program
{
    public static void Main()
    {
        CreateHostBuilder()
            .Build()
            .Run();
    }

    public static IHostBuilder CreateHostBuilder() => 
        Host.CreateDefaultBuilder()
            .UseSystemd()
            .ConfigureLogging(x => 
            {

            })
            .ConfigureServices((hostContext, services) => 
            {
                services.AddSingleton<IBootstrapper, gRPCServerBootstraper>();

                services.AddHostedService<Worker>();
            })
            .RegisterBootstrapperManager()
            .RegisterEventAggregatorManager()
            .RegisterTcpServer()
            .RegisterApplicationSettingsService()
            .RegisterBlockchainService()
            .RegisterServerService()
            .RegisterRpcModel()
            .RegisterRpcManager()
            .RegisterGrpc();
}

