using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Olimpo;
using HushServerNode.DbModel;
using HushNode.Blockchain;
using HushNode.Blockchain.Persistency.EntityFramework;
using HushNode.Credentials;
using HushNode.MemPool;
using HushNode.Blockchain.Persistency.Postgres;
using Microsoft.EntityFrameworkCore;

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
                // services.AddSingleton<IBootstrapper, gRPCServerBootstraper>();

                // services.AddDbContextFactory<HushNodeDbContext>();

                services.AddDbContext<HushNodeDbContext>(options =>
                {
                    options.UseNpgsql(hostContext.Configuration.GetConnectionString("HushNetworkDb"));
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
            .RegisterHushNodeBlockchain();

        public static IConfigurationBuilder ConfigureConfigurationBuilder(IConfigurationBuilder configurationBuilder)
        {
            configurationBuilder ??= new ConfigurationBuilder();

            configurationBuilder
                .AddJsonFile("ApplicationSettings.json");

            return configurationBuilder;
        }
}

