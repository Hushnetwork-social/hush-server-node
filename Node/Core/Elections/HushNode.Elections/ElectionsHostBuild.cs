using HushNode.Caching;
using HushNode.Elections.Storage;
using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Olimpo;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections;

public static class ElectionsHostBuild
{
    public static IHostBuilder RegisterCoreModuleElections(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton(CreateCeremonyOptions(hostContext.Configuration));
            services.AddSingleton(CreateBallotPublicationOptions(hostContext.Configuration));
            services.AddSingleton<IBootstrapper, ElectionCeremonyProfileRegistryBootstrapper>();
            services.AddSingleton<IBootstrapper, ElectionBallotPublicationBootstrapper>();
            services.RegisterElectionsStorageServices(hostContext);
            services.RegisterElectionsCoreServices();
        });

        return builder;
    }

    public static void RegisterElectionsCoreServices(this IServiceCollection services)
    {
        services.AddTransient<ITransactionDeserializerStrategy, EncryptedElectionEnvelopeDeserializerStrategy>();
        services.AddTransient<ITransactionDeserializerStrategy, CreateElectionDraftDeserializerStrategy>();
        services.AddTransient<ITransactionDeserializerStrategy, UpdateElectionDraftDeserializerStrategy>();
        services.AddTransient<ITransactionDeserializerStrategy, InviteElectionTrusteeDeserializerStrategy>();
        services.AddTransient<ITransactionDeserializerStrategy, RevokeElectionTrusteeInvitationDeserializerStrategy>();
        services.AddTransient<ITransactionDeserializerStrategy, StartElectionGovernedProposalDeserializerStrategy>();
        services.AddTransient<ITransactionDeserializerStrategy, ApproveElectionGovernedProposalDeserializerStrategy>();
        services.AddTransient<ITransactionDeserializerStrategy, RetryElectionGovernedProposalExecutionDeserializerStrategy>();
        services.AddTransient<ITransactionDeserializerStrategy, OpenElectionDeserializerStrategy>();
        services.AddTransient<ITransactionDeserializerStrategy, CloseElectionDeserializerStrategy>();
        services.AddTransient<ITransactionDeserializerStrategy, FinalizeElectionDeserializerStrategy>();
        services.AddTransient<ITransactionContentHandler, EncryptedElectionEnvelopeContentHandler>();
        services.AddTransient<IIndexStrategy, EncryptedElectionEnvelopeIndexStrategy>();
        services.AddTransient<IIndexStrategy, CreateElectionDraftIndexStrategy>();
        services.AddTransient<IIndexStrategy, UpdateElectionDraftIndexStrategy>();
        services.AddTransient<IIndexStrategy, InviteElectionTrusteeIndexStrategy>();
        services.AddTransient<IIndexStrategy, RevokeElectionTrusteeInvitationIndexStrategy>();
        services.AddTransient<IIndexStrategy, StartElectionGovernedProposalIndexStrategy>();
        services.AddTransient<IIndexStrategy, ApproveElectionGovernedProposalIndexStrategy>();
        services.AddTransient<IIndexStrategy, RetryElectionGovernedProposalExecutionIndexStrategy>();
        services.AddTransient<IIndexStrategy, OpenElectionIndexStrategy>();
        services.AddTransient<IIndexStrategy, CloseElectionIndexStrategy>();
        services.AddTransient<IIndexStrategy, FinalizeElectionIndexStrategy>();
        services.AddTransient<ICreateElectionDraftTransactionHandler, CreateElectionDraftTransactionHandler>();
        services.AddTransient<IUpdateElectionDraftTransactionHandler, UpdateElectionDraftTransactionHandler>();
        services.AddTransient<IInviteElectionTrusteeTransactionHandler, InviteElectionTrusteeTransactionHandler>();
        services.AddTransient<UpdateElectionDraftContentHandler>();
        services.AddTransient<InviteElectionTrusteeContentHandler>();
        services.AddTransient<RevokeElectionTrusteeInvitationContentHandler>();
        services.AddTransient<StartElectionGovernedProposalContentHandler>();
        services.AddTransient<ApproveElectionGovernedProposalContentHandler>();
        services.AddTransient<RetryElectionGovernedProposalExecutionContentHandler>();
        services.AddTransient<OpenElectionContentHandler>();
        services.AddTransient<CloseElectionContentHandler>();
        services.AddTransient<FinalizeElectionContentHandler>();
        services.AddTransient<IRevokeElectionTrusteeInvitationTransactionHandler, RevokeElectionTrusteeInvitationTransactionHandler>();
        services.AddTransient<IStartElectionGovernedProposalTransactionHandler, StartElectionGovernedProposalTransactionHandler>();
        services.AddTransient<IApproveElectionGovernedProposalTransactionHandler, ApproveElectionGovernedProposalTransactionHandler>();
        services.AddTransient<IRetryElectionGovernedProposalExecutionTransactionHandler, RetryElectionGovernedProposalExecutionTransactionHandler>();
        services.AddTransient<IOpenElectionTransactionHandler, OpenElectionTransactionHandler>();
        services.AddTransient<ICloseElectionTransactionHandler, CloseElectionTransactionHandler>();
        services.AddTransient<IFinalizeElectionTransactionHandler, FinalizeElectionTransactionHandler>();
        services.AddTransient<ICreateElectionDraftValidationService, CreateElectionDraftValidationService>();
        services.AddTransient<IElectionEnvelopeCryptoService, ElectionEnvelopeCryptoService>();
        services.AddSingleton<IElectionBallotPublicationCryptoService, ElectionBallotPublicationCryptoService>();
        services.AddSingleton<ElectionBallotPublicationService>();
        services.AddSingleton<IElectionBallotPublicationService>(sp => sp.GetRequiredService<ElectionBallotPublicationService>());
        services.AddSingleton<IElectionLifecycleService>(sp =>
            new ElectionLifecycleService(
                sp.GetRequiredService<IUnitOfWorkProvider<ElectionsDbContext>>(),
                sp.GetRequiredService<ILogger<ElectionLifecycleService>>(),
                sp.GetRequiredService<ElectionCeremonyOptions>(),
                sp.GetRequiredService<IElectionCastIdempotencyCacheService>()));
    }

    private static ElectionCeremonyOptions CreateCeremonyOptions(IConfiguration configuration) =>
        new(
            EnableDevCeremonyProfiles: configuration.GetValue(
                "Elections:Ceremony:EnableDevCeremonyProfiles",
                defaultValue: true),
            ApprovedRegistryRelativePath: configuration.GetValue(
                "Elections:Ceremony:ApprovedRegistryRelativePath",
                defaultValue: ElectionCeremonyProfileCatalog.GetDefaultRegistryRelativePath())!,
            RequiredRolloutVersion: configuration.GetValue(
                "Elections:Ceremony:RequiredRolloutVersion",
                defaultValue: ElectionCeremonyProfileCatalog.ExpectedVersion)!);

    private static ElectionBallotPublicationOptions CreateBallotPublicationOptions(IConfiguration configuration) =>
        new(
            HighWaterMark: configuration.GetValue("Elections:BallotPublication:HighWaterMark", 20),
            LowWaterMark: configuration.GetValue("Elections:BallotPublication:LowWaterMark", 10),
            MaxBatchPerBlock: configuration.GetValue("Elections:BallotPublication:MaxBatchPerBlock", 20));
}
