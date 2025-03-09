using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Olimpo;
using HushNode.Blockchain;
using HushNode.Blockchain.gRPC;
using HushNode.Blockchain.Repositories;
using HushNode.Credentials;
using HushNode.Indexing;
using HushNode.InternalModules.Bank;
using HushNode.InternalModules.Identity;
using HushNode.MemPool;

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
            .ConfigureLogging(x => { })
            .ConfigureServices((hostContext, services) => 
            {
                services.AddSingleton<IBootstrapper, gRPCServerBootstraper>();

                services.AddDbContext<HushNodeDbContext>((provider, options) =>
                {
                    options.UseNpgsql(hostContext.Configuration.GetConnectionString("HushNetworkDb"));
                    options.EnableSensitiveDataLogging();  // For debugging
                    options.EnableDetailedErrors();  // For debugging
                });

                services.AddHostedService<Worker>();
            })
            .RegisterBootstrapperManager()
            .RegisterEventAggregatorManager()
            .RegisterBlockchainRepositories()
            .RegisterHushNodeMemPool()
            .RegisterHushCredentials()
            // .RegisterInMemoryPersistency()
            // .RegisterPostgresPersistency()
            .RegisterHushNodeBlockchain()
            .RegisterHushNodeBlockchaingRPC()
            .RegisterHushNodeIndexing()
            .RegisterInternalModuleBank()
            .RegisterInternalModuleIdentity();

        public static IConfigurationBuilder ConfigureConfigurationBuilder(IConfigurationBuilder configurationBuilder)
        {
            configurationBuilder ??= new ConfigurationBuilder();

            configurationBuilder
                .AddJsonFile("ApplicationSettings.json");

            return configurationBuilder;
        }
}

