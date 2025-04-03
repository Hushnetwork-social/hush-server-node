using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using HushNode.Identity.Storage;
using HushNode.Identity.gRPC;
using HushNode.Indexing.Interfaces;
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
            services.AddTransient<IIndexStrategy, FullIdentityIndexStrategy>();

            services.AddScoped<IFullIdentityTransactionHandler, FullIdentityTransactionHandler>();

            services.RegisterIdentitygRPCServices();
            services.RegisterIdentityStorageServices(hostContext);
        });

        return builder;
    }
}
