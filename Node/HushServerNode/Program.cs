using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Olimpo;
using HushNode.Blockchain;
using HushNode.Blockchain.Persistency.EntityFramework;
using HushNode.Blockchain.Persistency.Postgres;
using HushNode.Credentials;
using HushNode.Indexing;
using HushNode.MemPool;
using HushNode.InternalModules.Bank;

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
                // services.AddSingleton<IBootstrapper, gRPCServerBootstraper>();

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
            .RegisterEntityFrameworkPersistency()
            .RegisterHushNodeMemPool()
            .RegisterHushCredentials()
            // .RegisterInMemoryPersistency()
            .RegisterPostgresPersistency()
            .RegisterHushNodeBlockchain()
            .RegisterHushNodeIndexing()
            .RegisterInternalModuleBank();

        public static IConfigurationBuilder ConfigureConfigurationBuilder(IConfigurationBuilder configurationBuilder)
        {
            configurationBuilder ??= new ConfigurationBuilder();

            configurationBuilder
                .AddJsonFile("ApplicationSettings.json");

            return configurationBuilder;
        }
}

