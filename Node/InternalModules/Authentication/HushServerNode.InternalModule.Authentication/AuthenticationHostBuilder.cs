using HushServerNode.Interfaces;
using HushServerNode.InternalModule.Authentication.Cache;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HushServerNode.InternalModule.Authentication;

public static class AuthenticationHostBuilder
{
    public static IHostBuilder RegisterInternalModuleAuthentication(this IHostBuilder hostBuilder)
    {
        return hostBuilder.ConfigureServices(services =>
        {
            // services.AddSingleton<IBootstrapper, BankBootstrapper>();

            services.AddDbContextFactory<CacheAuthenticationDbContext>();
            services.AddSingleton<IDbContextConfigurator, CacheAuthenticationDbContextConfigurator>();
            services.AddSingleton<CacheAuthenticationDbContextConfigurator>();

            services.AddSingleton<IAuthenticationService, AuthenticationService>();
            services.AddSingleton<IAuthenticationDbAccess, AuthenticationDbAccess>();

            // services.AddSingleton<IIndexStrategy, RewardIndexStrategy>();
        });
    }
}
