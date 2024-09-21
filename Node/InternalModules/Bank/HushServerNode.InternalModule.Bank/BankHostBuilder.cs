using HushNetwork.proto;
using HushServerNode.Interfaces;
using HushServerNode.InternalModule.Bank.Cache;
using HushServerNode.InternalModule.Bank.IndexStrategies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Olimpo;

namespace HushServerNode.InternalModule.Bank;

public static class BankHostBuilder
{
    public static IHostBuilder RegisterInternalModuleBank(this IHostBuilder hostBuilder)
     {
          return hostBuilder.ConfigureServices(services =>
          {
               services.AddSingleton<IBootstrapper, BankBootstrapper>();

               services.AddDbContextFactory<CacheBankDbContext>();
               services.AddSingleton<IDbContextConfigurator, CacheBankDbContextConfigurator>();
               services.AddSingleton<CacheBankDbContextConfigurator>();

               services.AddSingleton<IBankService, BankService>();

               services.AddSingleton<IGrpcDefinition, BankGrpcServiceDefinition>();
               services.AddSingleton<HushBank.HushBankBase, BankGrpcService>();

               services.AddSingleton<IIndexStrategy, RewardIndexStrategy>();
          });
     }
}
