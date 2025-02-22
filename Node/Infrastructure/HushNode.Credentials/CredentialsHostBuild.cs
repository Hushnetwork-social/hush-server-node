using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Olimpo;

namespace HushNode.Credentials;

public static class CredentialsHostBuild
{
    public static IHostBuilder RegisterHushCredentials(this IHostBuilder builder)
    {
        builder.ConfigureServices((context, services) =>
        {
            services.AddSingleton<IBootstrapper, CredentialsBoostrapper>();

            services.AddSingleton<ICredentialsProvider, CredentialsProvider>();

            services.Configure<CredentialsProfile>(
                context.Configuration.GetSection("CredentialsProfile"));
        });

        return builder;
    }
}
