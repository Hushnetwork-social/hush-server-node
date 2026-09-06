using HushNode.HushVoting.Licensing.Storage;
using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HushNode.HushVoting.Licence.Transactions;

/// <summary>
/// DI registration surface for the FEAT-015 licence transaction pipeline (codec, validator,
/// reservation store, admission gate, block-context index strategy). The HushServerNode host
/// supplies the concrete DbContext factory and validation context source (identity storage +
/// catalogue + current indexed state), so this module stays free of host types.
/// </summary>
public static class LicenceTransactionsHostBuild
{
    public static IServiceCollection AddLicenceTransactionPipeline(
        this IServiceCollection services,
        Func<DbContext> contextFactory,
        IHushVotingLicenceValidationContextSource contextSource,
        LicenceServiceConfiguration configuration,
        LicenceCacheOutboxPolicy? cacheOutbox = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(contextSource);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IHushVotingLicenceCanonicalSerializer, HushVotingLicenceCanonicalSerializer>();
        services.AddSingleton<IHushVotingLicenceSignatureVerifier, HushVotingLicenceSignatureVerifier>();
        services.AddSingleton<IHushVotingLicenceValidationContextSource>(contextSource);
        services.AddSingleton<IHushVotingLicenceTransactionValidator, HushVotingLicenceTransactionValidator>();

        services.AddTransient<ITransactionDeserializerStrategy, HushVotingLicenceDeserializerStrategy>();
        services.AddSingleton<IHushVotingLicenceReservationStore>(_ =>
            new HushVotingLicenceReservationStore(contextFactory));
        services.AddSingleton<IHushVotingLicenceAdmissionGate>(sp =>
            new HushVotingLicenceAdmissionService(
                sp.GetRequiredService<IHushVotingLicenceTransactionValidator>(),
                sp.GetRequiredService<IHushVotingLicenceValidationContextSource>(),
                sp.GetRequiredService<IHushVotingLicenceReservationStore>(),
                contextFactory));
        services.AddTransient<ITransactionContentHandler, HushVotingLicenceContentHandler>(
            sp => new HushVotingLicenceContentHandler(
                sp.GetRequiredService<HushNode.Credentials.ICredentialsProvider>(),
                sp.GetRequiredService<IHushVotingLicenceTransactionValidator>()));
        services.AddSingleton<IBlockContextIndexStrategy>(sp =>
            new LicenceBlockContextIndexStrategy(
                sp.GetRequiredService<IHushVotingLicenceTransactionValidator>(),
                sp.GetRequiredService<IHushVotingLicenceValidationContextSource>(),
                contextFactory,
                configuration,
                cacheOutbox));

        return services;
    }
}
