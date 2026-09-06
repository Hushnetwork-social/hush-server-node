using System.Reflection;
using System.Text.Json;
using HushNode.Caching;
using HushNode.Elections.HushVotingLicence;
using HushNode.HushVoting.Licence.Transactions;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HushServerNode.HushVotingLicensingIntegration;

/// <summary>
/// HushServerNode host composition for the FEAT-013 licensing authority. Wires one licensing
/// <c>IDbContextConfigurator</c> (module), the internal <c>LicenceEntitlementService</c>, the
/// typed <c>LicenceServiceConfiguration</c> sourced from the FEAT-012 immutable snapshot and its
/// release metadata, privacy-safe telemetry, and the fail-closed rollout-readiness bootstrapper.
/// No public endpoint, Redis, or client mutation is introduced (AC-013-022).
/// </summary>
public static class HushVotingLicensingIntegrationHostBuild
{
    public static IHostBuilder RegisterHushVotingLicensingIntegration(this IHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureServices((hostContext, services) =>
        {
            _ = hostContext;
            AddHushVotingLicensingIntegrationServices(services);
        });

        return builder;
    }

    /// <summary>DI surface for the licensing authority (exactly one configurator stays in the module registration).</summary>
    public static void AddHushVotingLicensingIntegrationServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(sp => BuildLicenceTelemetry(sp));
        services.AddSingleton(sp => BuildLicenceServiceConfiguration(sp));
        services.AddTransient<LicenceEntitlementService>(sp => new LicenceEntitlementService(
            () => CreateFreshDbContext(sp),
            sp.GetRequiredService<LicenceServiceConfiguration>(),
            TimeProvider.System,
            sp.GetService<LicenceTelemetry>(),
            sp.GetService<LicenceCacheOutboxPolicy>()));
        services.AddSingleton<ILicenceIndexedProjectionReader>(sp =>
            new LicenceIndexedProjectionReader(() => CreateFreshDbContext(sp)));
        services.AddSingleton<HushVotingLicenceRolloutReadinessBootstrapper>();
        services.AddSingleton<Olimpo.IBootstrapper, HushVotingLicenceRolloutReadinessBootstrapper>(
            sp => sp.GetRequiredService<HushVotingLicenceRolloutReadinessBootstrapper>());

        // FEAT-015: licence transaction pipeline (deserializer, validator, reservation, admission
        // gate, block-context index strategy, content handler). The validation context source
        // resolves the signatory's exact indexed identity + current catalogue + indexed state.
        RegisterLicenceTransactionPipeline(services);
    }

    /// <summary>Registers the FEAT-015 licence transaction pipeline components.</summary>
    public static void RegisterLicenceTransactionPipeline(IServiceCollection services)
    {
        services.AddSingleton<HushNode.HushVoting.Licence.Transactions.IHushVotingLicenceCanonicalSerializer,
            HushNode.HushVoting.Licence.Transactions.HushVotingLicenceCanonicalSerializer>();
        services.AddSingleton<HushNode.HushVoting.Licence.Transactions.IHushVotingLicenceSignatureVerifier,
            HushNode.HushVoting.Licence.Transactions.HushVotingLicenceSignatureVerifier>();
        services.AddSingleton<HostLicenceValidationContextSource>();
        services.AddSingleton<HushNode.HushVoting.Licence.Transactions.IHushVotingLicenceValidationContextSource>(
            sp => sp.GetRequiredService<HostLicenceValidationContextSource>());
        services.AddSingleton<HushNode.HushVoting.Licence.Transactions.IHushVotingLicenceTransactionValidator>(sp =>
            new HushNode.HushVoting.Licence.Transactions.HushVotingLicenceTransactionValidator(
                sp.GetRequiredService<HushNode.HushVoting.Licence.Transactions.IHushVotingLicenceCanonicalSerializer>(),
                sp.GetRequiredService<HushNode.HushVoting.Licence.Transactions.IHushVotingLicenceSignatureVerifier>(),
                sp.GetRequiredService<HushNode.HushVoting.Licence.Transactions.IHushVotingLicenceValidationContextSource>()));
        services.AddTransient<HushShared.Blockchain.TransactionModel.ITransactionDeserializerStrategy,
            HushNode.HushVoting.Licence.Transactions.HushVotingLicenceDeserializerStrategy>();
        services.AddSingleton<HushNode.HushVoting.Licence.Transactions.IHushVotingLicenceReservationStore>(sp =>
            new HushNode.HushVoting.Licence.Transactions.HushVotingLicenceReservationStore(
                () => CreateFreshDbContext(sp)));
        services.AddSingleton<HushNode.HushVoting.Licence.Transactions.IHushVotingLicenceAdmissionGate>(sp =>
            new HushNode.HushVoting.Licence.Transactions.HushVotingLicenceAdmissionService(
                sp.GetRequiredService<HushNode.HushVoting.Licence.Transactions.IHushVotingLicenceTransactionValidator>(),
                sp.GetRequiredService<HushNode.HushVoting.Licence.Transactions.IHushVotingLicenceValidationContextSource>(),
                sp.GetRequiredService<HushNode.HushVoting.Licence.Transactions.IHushVotingLicenceReservationStore>(),
                () => CreateFreshDbContext(sp)));
        services.AddSingleton<HushNode.Indexing.Interfaces.IBlockContextIndexStrategy>(sp =>
            new HushNode.HushVoting.Licence.Transactions.LicenceBlockContextIndexStrategy(
                sp.GetRequiredService<HushNode.HushVoting.Licence.Transactions.IHushVotingLicenceTransactionValidator>(),
                sp.GetRequiredService<HushNode.HushVoting.Licence.Transactions.IHushVotingLicenceValidationContextSource>(),
                () => CreateFreshDbContext(sp),
                sp.GetRequiredService<LicenceServiceConfiguration>(),
                sp.GetService<LicenceCacheOutboxPolicy>()));
        services.AddTransient<HushShared.Blockchain.TransactionModel.ITransactionContentHandler>(
            sp => new HushNode.HushVoting.Licence.Transactions.HushVotingLicenceContentHandler(
                sp.GetRequiredService<HushNode.Credentials.ICredentialsProvider>(),
                sp.GetRequiredService<HushNode.HushVoting.Licence.Transactions.IHushVotingLicenceTransactionValidator>()));
    }

    /// <summary>
    /// Builds the typed configuration from the FEAT-012 immutable snapshot and the release metadata
    /// companion file. Throws with the stable code when composition is invalid (fail closed); the
    /// catalogue loader has already verified the digest/version, so this is a defensive guard.
    /// </summary>
    public static LicenceServiceConfiguration BuildLicenceServiceConfiguration(IServiceProvider services) =>
        BuildLicenceServiceConfiguration(services, AppContext.BaseDirectory);

    public static LicenceServiceConfiguration BuildLicenceServiceConfiguration(
        IServiceProvider services,
        string contentRoot)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);

        var snapshot = services.GetRequiredService<HushVotingLicenceSnapshot>();
        var options = services.GetRequiredService<IOptions<HushVotingLicenceOptions>>().Value;
        var metadata = HushVotingLicenceReleaseMetadataReader.ReadFromContentRoot(contentRoot, options);

        if (metadata.IsValid
            && LicenceServiceConfiguration.TryCreate(
                snapshot.Catalogue.Version.Value,
                metadata.DigestSha256,
                metadata.SchemaId,
                snapshot.Catalogue,
                out var configuration,
                out var stableError)
            && configuration is not null)
        {
            return configuration;
        }

        throw new InvalidOperationException(
            $"HushVoting licence service configuration invalid: {metadata.SafeError ?? "unknown"}");
    }

    public static LicenceTelemetry BuildLicenceTelemetry(IServiceProvider services)
    {
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger("HushVoting.Licensing");
        return new LicenceTelemetry(logger);
    }

    /// <summary>
    /// Fresh <c>HushNodeDbContext</c> per entitlement attempt: the bounded executor discards a
    /// context after any transient/ambiguous failure, so attempts must never reuse a poisoned
    /// context. Shares the registered options and configurator set (exactly one licensing
    /// configurator remains in DI).
    /// </summary>
    public static HushNodeDbContext CreateFreshDbContext(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var configurators = services.GetServices<IDbContextConfigurator>().ToArray();
        var options = services.GetRequiredService<DbContextOptions<HushNodeDbContext>>();
        return new HushNodeDbContext(configurators, options);
    }

    /// <summary>Current server release + host provenance (privacy-safe; never an identity).</summary>
    public static (string ServerRelease, string ServerHost) ServerProvenance()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        return (version, Environment.MachineName);
    }

    /// <summary>
    /// Trusted-host boundary: constructs the server-created licence subject from the authenticated
    /// authority's canonical public signing address and authoritative identity creation block index.
    /// Raw client subject text can never reach the internal service without this conversion.
    /// </summary>
    public static AuthenticatedIdentitySubject? FromAuthenticatedIdentity(
        string publicSigningAddress,
        long identityCreationBlockIndex,
        out string? stableError)
    {
        ArgumentNullException.ThrowIfNull(publicSigningAddress);

        if (!AuthenticatedIdentitySubject.TryCreate(
                LicencePersistenceVocabulary.SubjectTypeIdentity,
                publicSigningAddress,
                identityCreationBlockIndex,
                out var subject,
                out stableError))
        {
            return null;
        }

        return subject;
    }
}

/// <summary>
/// Bounded reader for the release metadata companion file (digest/version/schema). The FEAT-012
/// loader verifies the digest against the manifest; this host reader only surfaces the verified
/// metadata as typed primitives for the licensing install spec and service configuration.
/// </summary>
public static class HushVotingLicenceReleaseMetadataReader
{
    public const int MaxReleaseMetadataBytes = 16 * 1024;
    private const string ReleaseMetadataFileName = "approved-licence-catalogue.release.json";

    public static readonly IReadOnlyList<string> RequiredFields =
    [
        "catalogueVersion",
        "digestSha256",
        "schemaId",
    ];

    public static HushVotingLicenceReleaseMetadataResult ReadFromContentRoot(
        string contentRoot,
        HushVotingLicenceOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentNullException.ThrowIfNull(options);

        var releasePath = ResolveReleasePath(contentRoot, options);
        if (releasePath is null)
        {
            return HushVotingLicenceReleaseMetadataResult.Failed(
                "Release metadata companion file could not be resolved beneath the content root.");
        }

        try
        {
            var bytes = File.ReadAllBytes(releasePath);
            if (bytes.Length > MaxReleaseMetadataBytes)
            {
                return HushVotingLicenceReleaseMetadataResult.Failed(
                    "Release metadata companion file exceeds the bounded read limit.");
            }

            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (!root.TryGetProperty("catalogueVersion", out var versionElement)
                || !root.TryGetProperty("digestSha256", out var digestElement)
                || !root.TryGetProperty("schemaId", out var schemaElement))
            {
                return HushVotingLicenceReleaseMetadataResult.Failed(
                    "Release metadata companion file is missing a required field.");
            }

            var version = versionElement.GetString() ?? string.Empty;
            var digest = digestElement.GetString() ?? string.Empty;
            var schema = schemaElement.GetString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(version)
                || string.IsNullOrWhiteSpace(digest)
                || string.IsNullOrWhiteSpace(schema))
            {
                return HushVotingLicenceReleaseMetadataResult.Failed(
                    "Release metadata companion file contains an empty required field.");
            }

            return HushVotingLicenceReleaseMetadataResult.Ok(version, digest, schema);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return HushVotingLicenceReleaseMetadataResult.Failed(
                $"Release metadata companion file could not be read: {exception.GetType().Name}");
        }
    }

    private static string? ResolveReleasePath(string contentRoot, HushVotingLicenceOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.CatalogueRelativePath))
        {
            return null;
        }

        var fullCatalogue = Path.GetFullPath(
            Path.Combine(contentRoot, options.CatalogueRelativePath),
            contentRoot);

        // The companion release file always sits beside the manifest.
        var directory = Path.GetDirectoryName(fullCatalogue);
        return directory is null
            ? null
            : Path.Combine(directory, ReleaseMetadataFileName);
    }
}

public sealed record HushVotingLicenceReleaseMetadataResult(
    bool IsValid,
    string CatalogueVersion,
    string SchemaId,
    string DigestSha256,
    string? SafeError)
{
    public static HushVotingLicenceReleaseMetadataResult Ok(
        string version, string digest, string schema) =>
        new(true, version, schema, digest, null);

    public static HushVotingLicenceReleaseMetadataResult Failed(string safeError) =>
        new(false, string.Empty, string.Empty, string.Empty, safeError);
}
