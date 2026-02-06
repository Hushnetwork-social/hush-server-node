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
            // FEAT-052: Configure FeedsSettings for pagination
            services.Configure<FeedsSettings>(hostContext.Configuration.GetSection(FeedsSettings.SectionName));

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

            // FEAT-008: Join/Leave Mechanics - JoinGroupFeed
            services.AddTransient<ITransactionDeserializerStrategy, JoinGroupFeedDeserializerStrategy>();
            services.AddTransient<IIndexStrategy, JoinGroupFeedIndexStrategy>();
            services.AddTransient<ITransactionContentHandler, JoinGroupFeedContentHandler>();
            services.AddTransient<IJoinGroupFeedTransactionHandler, JoinGroupFeedTransactionHandler>();

            // FEAT-008: Join/Leave Mechanics - AddMemberToGroupFeed
            services.AddTransient<ITransactionDeserializerStrategy, AddMemberToGroupFeedDeserializerStrategy>();
            services.AddTransient<IIndexStrategy, AddMemberToGroupFeedIndexStrategy>();
            services.AddTransient<ITransactionContentHandler, AddMemberToGroupFeedContentHandler>();
            services.AddTransient<IAddMemberToGroupFeedTransactionHandler, AddMemberToGroupFeedTransactionHandler>();

            // FEAT-008: Join/Leave Mechanics - LeaveGroupFeed
            services.AddTransient<ITransactionDeserializerStrategy, LeaveGroupFeedDeserializerStrategy>();
            services.AddTransient<IIndexStrategy, LeaveGroupFeedIndexStrategy>();
            services.AddTransient<ITransactionContentHandler, LeaveGroupFeedContentHandler>();
            services.AddTransient<ILeaveGroupFeedTransactionHandler, LeaveGroupFeedTransactionHandler>();

            // FEAT-009: Admin Controls - Block Member
            services.AddTransient<ITransactionDeserializerStrategy, BlockMemberDeserializerStrategy>();
            services.AddTransient<IIndexStrategy, BlockMemberIndexStrategy>();
            services.AddTransient<ITransactionContentHandler, BlockMemberContentHandler>();
            services.AddTransient<IBlockMemberTransactionHandler, BlockMemberTransactionHandler>();

            // FEAT-009: Admin Controls - Unblock Member
            services.AddTransient<ITransactionDeserializerStrategy, UnblockMemberDeserializerStrategy>();
            services.AddTransient<IIndexStrategy, UnblockMemberIndexStrategy>();
            services.AddTransient<ITransactionContentHandler, UnblockMemberContentHandler>();
            services.AddTransient<IUnblockMemberTransactionHandler, UnblockMemberTransactionHandler>();

            // FEAT-009: Admin Controls - Promote to Admin
            services.AddTransient<ITransactionDeserializerStrategy, PromoteToAdminDeserializerStrategy>();
            services.AddTransient<IIndexStrategy, PromoteToAdminIndexStrategy>();
            services.AddTransient<ITransactionContentHandler, PromoteToAdminContentHandler>();
            services.AddTransient<IPromoteToAdminTransactionHandler, PromoteToAdminTransactionHandler>();

            // FEAT-009: Admin Controls - Update Group Feed Title
            services.AddTransient<ITransactionDeserializerStrategy, UpdateGroupFeedTitleDeserializerStrategy>();
            services.AddTransient<IIndexStrategy, UpdateGroupFeedTitleIndexStrategy>();
            services.AddTransient<ITransactionContentHandler, UpdateGroupFeedTitleContentHandler>();
            services.AddTransient<IUpdateGroupFeedTitleTransactionHandler, UpdateGroupFeedTitleTransactionHandler>();

            // FEAT-009: Admin Controls - Update Group Feed Description
            services.AddTransient<ITransactionDeserializerStrategy, UpdateGroupFeedDescriptionDeserializerStrategy>();
            services.AddTransient<IIndexStrategy, UpdateGroupFeedDescriptionIndexStrategy>();
            services.AddTransient<ITransactionContentHandler, UpdateGroupFeedDescriptionContentHandler>();
            services.AddTransient<IUpdateGroupFeedDescriptionTransactionHandler, UpdateGroupFeedDescriptionTransactionHandler>();

            // FEAT-009: Admin Controls - Delete Group Feed
            services.AddTransient<ITransactionDeserializerStrategy, DeleteGroupFeedDeserializerStrategy>();
            services.AddTransient<IIndexStrategy, DeleteGroupFeedIndexStrategy>();
            services.AddTransient<ITransactionContentHandler, DeleteGroupFeedContentHandler>();
            services.AddTransient<IDeleteGroupFeedTransactionHandler, DeleteGroupFeedTransactionHandler>();

            // FEAT-010: Key Rotation System
            services.AddTransient<IKeyRotationService, KeyRotationService>();
            services.AddTransient<ITransactionDeserializerStrategy, GroupFeedKeyRotationDeserializerStrategy>();
            services.AddTransient<IIndexStrategy, GroupFeedKeyRotationIndexStrategy>();
            services.AddTransient<ITransactionContentHandler, GroupFeedKeyRotationContentHandler>();
            services.AddTransient<IGroupFeedKeyRotationTransactionHandler, GroupFeedKeyRotationTransactionHandler>();

            // FEAT-015: Ban/Unban System - Ban Member
            services.AddTransient<ITransactionDeserializerStrategy, BanFromGroupFeedDeserializerStrategy>();
            services.AddTransient<IIndexStrategy, BanFromGroupFeedIndexStrategy>();
            services.AddTransient<ITransactionContentHandler, BanFromGroupFeedContentHandler>();
            services.AddTransient<IBanFromGroupFeedTransactionHandler, BanFromGroupFeedTransactionHandler>();

            // FEAT-015: Ban/Unban System - Unban Member
            services.AddTransient<ITransactionDeserializerStrategy, UnbanFromGroupFeedDeserializerStrategy>();
            services.AddTransient<IIndexStrategy, UnbanFromGroupFeedIndexStrategy>();
            services.AddTransient<ITransactionContentHandler, UnbanFromGroupFeedContentHandler>();
            services.AddTransient<IUnbanFromGroupFeedTransactionHandler, UnbanFromGroupFeedTransactionHandler>();

            // FEAT-011: Group Feed Messaging - unified into NewFeedMessage* handlers (KeyGeneration field)
        });

        return builder;
    }
}
