using HushNode.Caching;
using HushNode.Credentials;
using HushNode.Identity.Storage;
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
            services.AddSingleton(CreateEnvelopeOptions(hostContext.Configuration));
            services.AddSingleton(CreateAdminOnlyProtectedTallyEnvelopeOptions(hostContext.Configuration));
            services.AddSingleton(CreateCloseCountingExecutorEnvelopeOptions(hostContext.Configuration));
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
        services.AddSingleton<IElectionReportPackageService, ElectionReportPackageService>();
        services.AddSingleton<IElectionBallotPublicationCryptoService, ElectionBallotPublicationCryptoService>();
        services.AddSingleton<IElectionResultCryptoService, ElectionResultCryptoService>();
        services.AddSingleton<ICloseCountingExecutorEnvelopeCrypto>(sp =>
            CloseCountingExecutorEnvelopeCryptoFactory.Create(
                sp.GetRequiredService<CloseCountingExecutorEnvelopeCryptoOptions>()));
        services.AddSingleton<IAdminOnlyProtectedTallyEnvelopeCrypto>(sp =>
            AdminOnlyProtectedTallyEnvelopeCryptoFactory.Create(
                sp.GetRequiredService<AdminOnlyProtectedTallyEnvelopeCryptoOptions>()));
        services.AddSingleton<ICloseCountingExecutorKeyRegistry, InMemoryCloseCountingExecutorKeyRegistry>();
        services.AddSingleton<ElectionBallotPublicationService>();
        services.AddSingleton<IElectionBallotPublicationService>(sp => sp.GetRequiredService<ElectionBallotPublicationService>());
        services.AddSingleton<IElectionLifecycleService>(sp =>
            new ElectionLifecycleService(
                sp.GetRequiredService<IUnitOfWorkProvider<ElectionsDbContext>>(),
                sp.GetRequiredService<ILogger<ElectionLifecycleService>>(),
                sp.GetRequiredService<ElectionCeremonyOptions>(),
                sp.GetRequiredService<IElectionCastIdempotencyCacheService>(),
                sp.GetRequiredService<IElectionResultCryptoService>(),
                sp.GetRequiredService<IElectionReportPackageService>(),
                sp.GetRequiredService<ICredentialsProvider>(),
                sp.GetService<IIdentityService>(),
                sp.GetRequiredService<ICloseCountingExecutorKeyRegistry>(),
                sp.GetRequiredService<ICloseCountingExecutorEnvelopeCrypto>(),
                sp.GetRequiredService<IAdminOnlyProtectedTallyEnvelopeCrypto>()));
        services.AddHostedService<TallyExecutorBackgroundService>();
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

    private static ElectionEnvelopeOptions CreateEnvelopeOptions(IConfiguration configuration) =>
        new(
            AllowLegacyNodeEncryptedEnvelopeValidation: configuration.GetValue(
                "Elections:Envelope:AllowLegacyNodeEncryptedEnvelopeValidation",
                defaultValue: true),
            AllowLegacyNodeEncryptedParticipantResultMaterial: configuration.GetValue(
                "Elections:Envelope:AllowLegacyNodeEncryptedParticipantResultMaterial",
                defaultValue: true));

    private static AdminOnlyProtectedTallyEnvelopeCryptoOptions CreateAdminOnlyProtectedTallyEnvelopeOptions(
        IConfiguration configuration) =>
        new(
            Provider: GetConfigValue(
                configuration,
                "Elections:AdminOnlyProtectedTallyEnvelope:Provider",
                "HUSH_ELECTIONS_ADMIN_ONLY_ENVELOPE_PROVIDER")
                ?? AdminOnlyProtectedTallyEnvelopeCryptoOptions.ProviderAuto,
            AwsKmsKeyId: GetConfigValue(
                configuration,
                "Elections:AdminOnlyProtectedTallyEnvelope:AwsKmsKeyId",
                "HUSH_ELECTIONS_ADMIN_ONLY_KMS_KEY_ID"),
            AwsKmsRegion: GetConfigValue(
                configuration,
                "Elections:AdminOnlyProtectedTallyEnvelope:AwsKmsRegion",
                "HUSH_ELECTIONS_ADMIN_ONLY_KMS_REGION",
                "AWS_REGION",
                "AWS_DEFAULT_REGION"),
            AwsKmsServiceUrl: GetConfigValue(
                configuration,
                "Elections:AdminOnlyProtectedTallyEnvelope:AwsKmsServiceUrl",
                "HUSH_ELECTIONS_ADMIN_ONLY_KMS_SERVICE_URL"),
            AwsKmsServiceIdentityLabel: GetConfigValue(
                configuration,
                "Elections:AdminOnlyProtectedTallyEnvelope:AwsKmsServiceIdentityLabel",
                "HUSH_ELECTIONS_ADMIN_ONLY_KMS_SERVICE_IDENTITY"));

    private static CloseCountingExecutorEnvelopeCryptoOptions CreateCloseCountingExecutorEnvelopeOptions(
        IConfiguration configuration) =>
        new(
            Provider: GetConfigValue(
                configuration,
                "Elections:CloseCountingExecutorEnvelope:Provider",
                "HUSH_ELECTIONS_CLOSE_COUNTING_EXECUTOR_ENVELOPE_PROVIDER")
                ?? CloseCountingExecutorEnvelopeCryptoOptions.ProviderAuto,
            AwsKmsKeyId: GetConfigValue(
                configuration,
                "Elections:CloseCountingExecutorEnvelope:AwsKmsKeyId",
                "HUSH_ELECTIONS_CLOSE_COUNTING_EXECUTOR_KMS_KEY_ID"),
            AwsKmsRegion: GetConfigValue(
                configuration,
                "Elections:CloseCountingExecutorEnvelope:AwsKmsRegion",
                "HUSH_ELECTIONS_CLOSE_COUNTING_EXECUTOR_KMS_REGION",
                "AWS_REGION",
                "AWS_DEFAULT_REGION"),
            AwsKmsServiceUrl: GetConfigValue(
                configuration,
                "Elections:CloseCountingExecutorEnvelope:AwsKmsServiceUrl",
                "HUSH_ELECTIONS_CLOSE_COUNTING_EXECUTOR_KMS_SERVICE_URL"),
            AwsKmsServiceIdentityLabel: GetConfigValue(
                configuration,
                "Elections:CloseCountingExecutorEnvelope:AwsKmsServiceIdentityLabel",
                "HUSH_ELECTIONS_CLOSE_COUNTING_EXECUTOR_KMS_SERVICE_IDENTITY"));

    private static string? GetConfigValue(
        IConfiguration configuration,
        string key,
        params string[] environmentVariableNames)
    {
        var configured = configuration.GetValue<string>(key);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        foreach (var variableName in environmentVariableNames)
        {
            var value = Environment.GetEnvironmentVariable(variableName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
