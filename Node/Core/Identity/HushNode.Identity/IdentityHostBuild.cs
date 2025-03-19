using Microsoft.Extensions.Hosting;
using HushNode.Identity.Storage;
using HushNode.Identity.gRPC;

namespace HushNode.Identity;

public static class IdentityHostBuild
{
    public static IHostBuilder RegisterInternalModuleIdentity(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) =>
        {
            services.RegisterIdentitygRPCServices();
            services.RegisterIdentityStorageServices(hostContext);
        });

        return builder;
    }
}
