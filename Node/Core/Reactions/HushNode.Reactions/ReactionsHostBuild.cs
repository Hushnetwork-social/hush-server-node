using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Olimpo;
using HushNode.Indexing.Interfaces;
using HushNode.Feeds.Storage;
using HushNode.Reactions.Crypto;
using HushNode.Reactions.Storage;
using HushNode.Reactions.ZK;
using HushNode.Reactions.gRPC;
using HushShared.Blockchain.TransactionModel;

namespace HushNode.Reactions;

public static class ReactionsHostBuild
{
    public static IHostBuilder RegisterCoreModuleReactions(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IBootstrapper, ReactionsBootstrapper>();

            services.RegisterReactionsStorageServices(hostContext);
            services.RegisterReactionsRPCServices();
            services.RegisterReactionsCryptoServices(hostContext.Configuration);
            services.RegisterReactionsCoreServices();
        });

        return builder;
    }

    public static void RegisterReactionsCryptoServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IBabyJubJub, BabyJubJubCurve>();
        services.AddSingleton<IPoseidonHash, PoseidonHash>();

        // Use DevModeVerifier if Reactions:DevMode is true
        var devMode = configuration.GetValue<bool>("Reactions:DevMode", false);
        if (devMode)
        {
            services.AddSingleton<IZkVerifier, DevModeVerifier>();
        }
        else
        {
            services.AddSingleton<IZkVerifier, Groth16Verifier>();
        }
    }

    public static void RegisterReactionsCoreServices(this IServiceCollection services)
    {
        services.AddSingleton<IMembershipService, MembershipService>();
        services.AddSingleton<IReactionService, ReactionService>();
        services.AddSingleton<IUserCommitmentService, UserCommitmentService>();

        // Handler to register local user's commitment when they join a feed
        services.AddSingleton<FeedCreatedCommitmentHandler>();

        // FEAT-012: GroupFeedInfoProvider for Protocol Omega integration with Group Feeds
        // Replaces StubFeedInfoProvider with real implementation that queries Feeds storage
        services.AddSingleton<IFeedInfoProvider, GroupFeedInfoProvider>();

        // FEAT-012: Reaction key derivation service for Group Feeds
        services.AddSingleton<IReactionKeyDerivationService, ReactionKeyDerivationService>();

        // Blockchain transaction infrastructure for reactions
        services.AddTransient<ITransactionDeserializerStrategy, NewReactionDeserializeStrategy>();
        services.AddTransient<ITransactionContentHandler, NewReactionContentHandler>();
        services.AddTransient<IIndexStrategy, NewReactionIndexStrategy>();
        services.AddTransient<IReactionTransactionHandler, ReactionTransactionHandler>();
    }
}
