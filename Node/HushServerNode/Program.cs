using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Olimpo;
// using HushEcosystem;
using Microsoft.Extensions.Configuration;
// using HushServerNode.InternalModule.Blockchain;
using HushServerNode.DbModel;
using HushNetwork.BlockchainWorkflows;
// using HushServerNode.InternalModule.Bank;
// using HushServerNode.InternalModule.MemPool;
// using HushServerNode.InternalModule.Authentication;
// using HushServerNode.InternalModule.Feed;

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
            .RegisterBlockchainWorkflow();
            // .RegisterInternalModuleBlockchain()
            // .RegisterInternalModuleBank()
            // .RegisterInternalModuleAuthentication()
            // .RegisterInternalModuleMemPool()
            // .RegisterInternalModuleFeed()
            // .RegisterTransactionDeserializerModel();

        public static IConfigurationBuilder ConfigureConfigurationBuilder(IConfigurationBuilder configurationBuilder)
        {
            configurationBuilder ??= new ConfigurationBuilder();

            configurationBuilder
                .AddJsonFile("ApplicationSettings.json");

            return configurationBuilder;
        }
}

