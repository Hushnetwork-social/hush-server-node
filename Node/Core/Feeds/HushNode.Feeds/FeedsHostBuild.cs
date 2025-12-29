using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Olimpo;
using HushNode.Indexing.Interfaces;
using HushNode.Feeds.Storage;
using HushNode.Feeds.gRPC;
using HushShared.Blockchain.TransactionModel;

namespace HushNode.Feeds;

public static class FeedsHostBuild
{
    public static IHostBuilder RegisterCoreModuleFeeds(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IBootstrapper, FeedsBootstrapper>();

            services.AddSingleton<IFeedsInitializationWorkflow, FeedsInitializationWorkflow>();
            services.AddSingleton<INewPersonalFeedTransactionHandler, NewPersonalFeedTransactionHandler>();

            services.RegisterFeedsStorageServices(hostContext);

            services.AddTransient<ITransactionDeserializerStrategy, NewPersonalFeedDeserializerStrategy>();
            services.AddTransient<IIndexStrategy, NewPersonalFeedIndexStrategy>(); 
            services.AddTransient<ITransactionContentHandler, NewPersonalFeedContentHandler>(); 

            services.RegisterFeedsRPCServices();

            services.AddTransient<ITransactionDeserializerStrategy, NewFeedMessageDeserializeStrategy>();
            services.AddTransient<ITransactionContentHandler, NewFeedMessageContentHandler>();
            services.AddTransient<IIndexStrategy, NewFeedMessageIndexStrategy>();
            services.AddTransient<IFeedMessageTransactionHandler, FeedMessageTransactionHandler>();
            
            services.AddTransient<ITransactionDeserializerStrategy, NewChatFeedDeserializerStrategy>();
            services.AddTransient<IIndexStrategy, NewChatFeedIndexStrategy>();
            services.AddTransient<ITransactionContentHandler, NewChatFeedContentHandler>();
            services.AddTransient<INewChatFeedTransactionHandler, NewChatFeedTransactionHandler>();

            // Group Feed services
            services.AddTransient<ITransactionDeserializerStrategy, NewGroupFeedDeserializerStrategy>();
            services.AddTransient<IIndexStrategy, NewGroupFeedIndexStrategy>();
            services.AddTransient<ITransactionContentHandler, NewGroupFeedContentHandler>();
            services.AddTransient<INewGroupFeedTransactionHandler, NewGroupFeedTransactionHandler>();
        });

        return builder;
    }
}
