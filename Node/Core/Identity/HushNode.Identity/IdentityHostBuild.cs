using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Olimpo;
using HushNode.Identity.Storage;
using HushNode.Identity.gRPC;
using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Identity.Model;

namespace HushNode.Identity;

public static class IdentityHostBuild
{
    public static IHostBuilder RegisterInternalModuleIdentity(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IBootstrapper, IdentityBootstrapper>();

            services.AddTransient<IIdentityService, IdentityService>();

            services.AddSingleton<IIdentityInitializationWorkflow, IdentityInitializationWorkflow>();

            services.AddTransient<ITransactionDeserializerStrategy, FullIdentityDeserializerStrategy>();
            services.AddTransient<ITransactionContentHandler, FullIdentityContentHandler>();
            services.AddTransient<IIndexStrategy, FullIdentityIndexStrategy>();

            services.AddSingleton<IFullIdentityTransactionHandler, FullIdentityTransactionHandler>();

            // FEAT-011: canonical FullIdentity validation + admission contracts
            services.AddSingleton<IFullIdentityCanonicalSerializer, FullIdentityCanonicalSerializer>();
            services.AddSingleton<IFullIdentitySignatureVerifier, FullIdentitySignatureVerifier>();
            services.AddSingleton<IFullIdentityValidator, FullIdentityValidator>();
            services.AddSingleton<IFullIdentityReservationService, FullIdentityAdmissionService>();
            services.AddSingleton<IFullIdentityAdmissionService, FullIdentityAdmissionService>();

            // UpdateIdentity transaction support
            services.AddTransient<ITransactionDeserializerStrategy, UpdateIdentityDeserializerStrategy>();
            services.AddTransient<ITransactionContentHandler, UpdateIdentityContentHandler>();
            services.AddTransient<IIndexStrategy, UpdateIdentityIndexStrategy>();

            services.AddSingleton<IUpdateIdentityTransactionHandler, UpdateIdentityTransactionHandler>();

            services.RegisterIdentitygRPCServices();
            services.RegisterIdentityStorageServices(hostContext);
        });

        return builder;
    }
}
