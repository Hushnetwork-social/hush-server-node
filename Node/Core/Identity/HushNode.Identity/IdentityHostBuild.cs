using Microsoft.Extensions.Hosting;
using HushNode.Identity.Storage;
using HushNode.Identity.gRPC;
using Microsoft.Extensions.DependencyInjection;
using HushShared.Blockchain.TransactionModel;

namespace HushNode.Identity;

public static class IdentityHostBuild
{
    public static IHostBuilder RegisterInternalModuleIdentity(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddTransient<ITransactionDeserializerStrategy, FullIdentityDeserializerStrategy>();
            services.AddTransient<ITransactionContentHandler, FullIdentityContentHandler>();

            services.RegisterIdentitygRPCServices();
            services.RegisterIdentityStorageServices(hostContext);
        });

        return builder;
    }
}
