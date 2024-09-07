using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Olimpo;
using HushEcosystem;
using Microsoft.Extensions.Configuration;
using HushServerNode.DbModel;
using HushServerNode.Cache.Authentication;
using HushServerNode.Cache.Bank;
using HushServerNode.Cache.Feed;
using HushServerNode.Cache.Blockchain;

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
            .ConfigureAppConfiguration(builder => ConfigureConfigurationBuilder(builder))
            .ConfigureLogging(x => 
            {

            })
            .ConfigureServices((hostContext, services) => 
            {
                services.AddSingleton<IBootstrapper, gRPCServerBootstraper>();

                services.AddDbContextFactory<HushNodeDbContext>();

                services.AddHostedService<Worker>();
            })
            .RegisterBootstrapperManager()
            .RegisterEventAggregatorManager()
            .RegisterTcpServer()
            .RegisterApplicationSettingsService()

            .RegisterBlockchainCacheService()
            .RegisterCacheBlockchainContext()           // The registration of the modules should be done by AutoDiscover and not HARDCODED
            .RegisterCacheAuthenticationContext()
            .RegisterCacheBankContext()
            .RegisterCacheFeedContext()
            
            .RegisterBlockchainService()
            .RegisterServerService()
            .RegisterRpcModel()
            .RegisterRpcManager()
            .RegisterGrpc();

        public static IConfigurationBuilder ConfigureConfigurationBuilder(IConfigurationBuilder configurationBuilder)
        {
            configurationBuilder ??= new ConfigurationBuilder();

            configurationBuilder
                .AddJsonFile("ApplicationSettings.json");

            return configurationBuilder;
        }
}

