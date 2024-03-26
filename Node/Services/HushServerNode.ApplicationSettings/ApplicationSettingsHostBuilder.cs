using System;
using HushServerNode.ApplicationSettings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Olimpo;

namespace HushServerNode;

public static class ApplicationSettingsHostBuilder
{
    public static IHostBuilder RegisterApplicationSettingsService(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IApplicationSettingsService, ApplicationSettingsService>();
            
            services.AddSingleton<IBootstrapper>(x => 
            {
                if (x.GetService<IApplicationSettingsService>() is not IBootstrapper bootstrapper)
                {
                    throw new InvalidOperationException("ApplicationSettings doesn't implement IBootstrapper interface");
                }

                return bootstrapper;
            });
        });

        return builder;
    }
}
