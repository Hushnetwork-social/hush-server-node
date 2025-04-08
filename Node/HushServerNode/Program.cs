using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Olimpo;
using HushNode.Blockchain;
using HushNode.Credentials;
using HushNode.Indexing;
using HushNode.Identity;
using HushNode.MemPool;
using HushNode.Bank;
using HushNode.Feeds;
using HushShared.Caching;

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
                // services.ConfigureOptions<>

                services.AddSingleton<IBootstrapper, gRPCServerBootstraper>();

                services.AddSingleton<IBlockchainCache, BlockchainCache>();

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
            .RegisterHushCredentials()
            .RegisterCoreModuleBlockchain()
            .RegisterCoreModuleBank()
            .RegisterCoreModuleFeeds()
            .RegisterHushNodeIndexing()
            .RegisterHushNodeMemPool()
            .RegisterInternalModuleIdentity();

        public static IConfigurationBuilder ConfigureConfigurationBuilder(IConfigurationBuilder configurationBuilder)
        {
            configurationBuilder ??= new ConfigurationBuilder();

            configurationBuilder
                .AddJsonFile("ApplicationSettings.json");

            return configurationBuilder;
        }
}

