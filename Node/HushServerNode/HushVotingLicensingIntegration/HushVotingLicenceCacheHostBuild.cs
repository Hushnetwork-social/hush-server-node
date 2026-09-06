using HushNode.HushVoting.Licensing.Cache;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.Notifications.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace HushServerNode.HushVotingLicensingIntegration;

/// <summary>
/// HushServerNode host composition for the FEAT-014 licence display cache (Phase 6). Binds
/// non-secret cache options, builds the validated current/previous HMAC key ring from external
/// secrets, and reuses the existing shared <c>IConnectionMultiplexer</c> plus
/// <c>RedisSettings.InstanceName</c> prefix. When caching is enabled the reader, Redis store,
/// telemetry, outbox store/dispatcher and the bounded outbox worker are registered exactly once;
/// when disabled, FEAT-013 remains operational and only cache-disabled readiness is reported.
/// Invalid enabled security configuration fails readiness with a stable code (no secret logged).
/// </summary>
public static class HushVotingLicenceCacheHostBuild
{
    public const string OptionsSectionName = "HushVotingLicenceCache";

    public static IHostBuilder RegisterHushVotingLicenceCache(this IHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ConfigureServices((hostContext, services) =>
            AddHushVotingLicenceCacheServices(services, hostContext.Configuration));
        return builder;
    }

    /// <summary>Explicit DI seam so host tests can assert registrations without building the host.</summary>
    public static IServiceCollection AddHushVotingLicenceCacheServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var cacheConfig = configuration.GetSection(OptionsSectionName);
        var enabled = cacheConfig.GetValue("Enabled", defaultValue: false);
        if (!enabled)
        {
            return services;
        }

        var options = BindOptions(cacheConfig);
        var optionsError = options.Validate();
        var keyRing = optionsError is null ? BuildKeyRing(cacheConfig, options, out optionsError) : null;

        if (optionsError is not null)
        {
            // Invalid enabled-cache security configuration fails readiness; never silently disable.
            throw new InvalidOperationException($"Licence cache configuration invalid: {optionsError}");
        }

        services.AddSingleton(options);
        services.AddSingleton(keyRing!);
        services.AddSingleton(sp =>
            new LicenceCacheOutboxPolicy(
                enabled: true,
                committedPublisher: (subject, entitlement, ct) =>
                    sp.GetRequiredService<LicenceCacheOutboxDispatcherService>()
                        .TryPublishCommittedAsync(subject, entitlement, ct)));
        services.AddSingleton<LicenceCacheEnvelopeCodec>();
        services.AddSingleton(sp => new LicenceCacheValueValidator(
            sp.GetRequiredService<LicenceCacheEnvelopeCodec>(), options));
        services.AddSingleton<LicenceCacheCircuitBreaker>(_ =>
            new LicenceCacheCircuitBreaker(() => DateTime.UtcNow, options));
        services.AddSingleton<LicenceCacheSingleFlight>();
        services.AddSingleton<LicenceCacheTelemetry>();
        services.AddSingleton<ICurrentLicenceCatalogueProvider>(sp =>
            new HostLicenceCatalogueProvider(sp.GetRequiredService<LicenceServiceConfiguration>()));
        services.AddSingleton<IEntitlementAuthorityResolver>(sp =>
            new LicenceIndexedEntitlementAuthorityResolver(
                sp.GetRequiredService<ILicenceIndexedProjectionReader>(),
                () => DateTime.UtcNow));

        services.AddSingleton<IEntitlementProjectionStore>(sp =>
        {
            var multiplexer = sp.GetRequiredService<IConnectionMultiplexer>();
            return new RedisEntitlementProjectionStore(
                multiplexer.GetDatabase(),
                options,
                sp.GetRequiredService<LicenceCacheEnvelopeCodec>());
        });

        services.AddSingleton<LicenceCacheRedisWriter>(sp => BuildRedisWriter(sp, options, keyRing!));
        services.AddSingleton<ILicenceCacheOutboxStore>(sp => new LicenceCacheOutboxStore(
            () => HushVotingLicensingIntegrationHostBuild.CreateFreshDbContext(sp), options));
        services.AddSingleton<LicenceCacheOutboxDispatcherService>(sp => BuildDispatcher(sp, options));
        services.AddSingleton<ICachedEntitlementReader>(sp => BuildCachedReader(sp, options, keyRing!));
        services.AddHostedService(sp =>
            new LicenceCacheOutboxWorker(
                sp.GetRequiredService<LicenceCacheOutboxDispatcherService>(),
                sp.GetRequiredService<ILogger<LicenceCacheOutboxWorker>>()));

        return services;
    }

    private static LicenceCacheOptions BindOptions(IConfigurationSection section)
    {
        var options = new LicenceCacheOptions();
        var enabled = section.GetValue("Enabled", defaultValue: options.Enabled);
        if (bool.TryParse(section["Enabled"], out var parsed))
        {
            enabled = parsed;
        }

        return new LicenceCacheOptions
        {
            Enabled = enabled,
            MaxTtlDays = section.GetValue("MaxTtlDays", options.MaxTtlDays),
            MaxTtlJitterPercent = section.GetValue("MaxTtlJitterPercent", options.MaxTtlJitterPercent),
            PreviousKeyOverlapMaxDays = section.GetValue("PreviousKeyOverlapMaxDays", options.PreviousKeyOverlapMaxDays),
            FillLeaseSeconds = section.GetValue("FillLeaseSeconds", options.FillLeaseSeconds),
            WaiterPollBudgetMs = section.GetValue("WaiterPollBudgetMs", options.WaiterPollBudgetMs),
            CircuitOpenFailureCount = section.GetValue("CircuitOpenFailureCount", options.CircuitOpenFailureCount),
            CircuitOpenSeconds = section.GetValue("CircuitOpenSeconds", options.CircuitOpenSeconds),
            MaxEnvelopeBytes = section.GetValue("MaxEnvelopeBytes", options.MaxEnvelopeBytes),
            DeliveredRetentionDays = section.GetValue("DeliveredRetentionDays", options.DeliveredRetentionDays),
            OutboxClaimBatchSize = section.GetValue("OutboxClaimBatchSize", options.OutboxClaimBatchSize),
            OutboxHealthWarningOldestAge = section.GetValue("OutboxHealthWarningOldestAge", options.OutboxHealthWarningOldestAge),
            OutboxHealthWarningDepth = section.GetValue("OutboxHealthWarningDepth", options.OutboxHealthWarningDepth),
            OutboxHealthCriticalOldestAge = section.GetValue("OutboxHealthCriticalOldestAge", options.OutboxHealthCriticalOldestAge),
            OutboxHealthCriticalDepth = section.GetValue("OutboxHealthCriticalDepth", options.OutboxHealthCriticalDepth),
            MinKeyIdCharacters = section.GetValue("MinKeyIdCharacters", options.MinKeyIdCharacters),
            MaxKeyIdCharacters = section.GetValue("MaxKeyIdCharacters", options.MaxKeyIdCharacters),
            MinMasterKeyBytes = section.GetValue("MinMasterKeyBytes", options.MinMasterKeyBytes),
        };
    }

    private static LicenceCacheKeyRing? BuildKeyRing(
        IConfigurationSection section,
        LicenceCacheOptions options,
        out string? errorCode)
    {
        errorCode = null;

        if (!TryReadKey(section, "Current", out var currentId, out var currentBytes, out var currentStarted))
        {
            errorCode = LicenceCacheOptionErrorCodes.MissingCurrentKey;
            return null;
        }

        var current = CreateMasterKey(currentId, currentBytes, currentStarted, options, out var currentError);
        if (current is null)
        {
            errorCode = currentError ?? LicenceCacheReasonCodes.InvalidKeyId;
            return null;
        }

        LicenceCacheMasterKey? previous = null;
        if (TryReadKey(section, "Previous", out var previousId, out var previousBytes, out var previousStarted))
        {
            previous = CreateMasterKey(previousId, previousBytes, previousStarted, options, out var previousError);
            if (previous is null)
            {
                errorCode = previousError ?? LicenceCacheReasonCodes.InvalidKeyId;
                return null;
            }
        }

        var ring = LicenceCacheKeyRing.TryCreate(current, previous, options, out var ringError);
        if (ring is null)
        {
            errorCode = ringError;
            return null;
        }

        return ring;
    }

    private static LicenceCacheMasterKey? CreateMasterKey(
        string keyId,
        byte[] keyBytes,
        DateTime rotationStartedUtc,
        LicenceCacheOptions options,
        out string? errorCode)
    {
        errorCode = null;
        try
        {
            return LicenceCacheMasterKey.Create(keyId, keyBytes, rotationStartedUtc, options, out errorCode);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool TryReadKey(
        IConfigurationSection section,
        string prefix,
        out string keyId,
        out byte[] keyBytes,
        out DateTime rotationStartedUtc)
    {
        keyId = string.Empty;
        keyBytes = Array.Empty<byte>();
        rotationStartedUtc = DateTime.MinValue;

        var keySection = section.GetSection(prefix);
        keyId = keySection["KeyId"] ?? string.Empty;
        var secret = keySection["SecretBase64"];
        var rotation = keySection["RotationStartedUtc"];
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(rotation))
        {
            return false;
        }

        try
        {
            keyBytes = Convert.FromBase64String(secret);
        }
        catch (FormatException)
        {
            return false;
        }

        return DateTime.TryParse(rotation, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out rotationStartedUtc);
    }

    private static LicenceCacheRedisWriter BuildRedisWriter(
        IServiceProvider sp,
        LicenceCacheOptions options,
        LicenceCacheKeyRing keyRing)
    {
        var redisSettings = sp.GetRequiredService<IOptions<RedisSettings>>().Value;
        return new LicenceCacheRedisWriter(
            sp.GetRequiredService<IEntitlementProjectionStore>(),
            sp.GetRequiredService<LicenceCacheEnvelopeCodec>(),
            options,
            keyRing,
            sp.GetRequiredService<ICurrentLicenceCatalogueProvider>(),
            sp.GetRequiredService<LicenceCacheTelemetry>(),
            () => DateTime.UtcNow,
            redisSettings.InstanceName);
    }

    private static LicenceCacheOutboxDispatcherService BuildDispatcher(
        IServiceProvider sp,
        LicenceCacheOptions options)
    {
        var redisSettings = sp.GetRequiredService<IOptions<RedisSettings>>().Value;
        return new LicenceCacheOutboxDispatcherService(
            sp.GetRequiredService<ILicenceCacheOutboxStore>(),
            () => HushVotingLicensingIntegrationHostBuild.CreateFreshDbContext(sp),
            sp.GetRequiredService<IEntitlementAuthorityResolver>(),
            sp.GetRequiredService<LicenceCacheRedisWriter>(),
            options,
            sp.GetRequiredService<LicenceCacheTelemetry>());
    }

    private static LicenceEntitlementCachedReader BuildCachedReader(
        IServiceProvider sp,
        LicenceCacheOptions options,
        LicenceCacheKeyRing keyRing)
    {
        var redisSettings = sp.GetRequiredService<IOptions<RedisSettings>>().Value;
        return new LicenceEntitlementCachedReader(
            sp.GetRequiredService<IEntitlementProjectionStore>(),
            sp.GetRequiredService<LicenceCacheValueValidator>(),
            sp.GetRequiredService<LicenceCacheEnvelopeCodec>(),
            options,
            keyRing,
            sp.GetRequiredService<IEntitlementAuthorityResolver>(),
            sp.GetRequiredService<ICurrentLicenceCatalogueProvider>(),
            sp.GetRequiredService<LicenceCacheCircuitBreaker>(),
            sp.GetRequiredService<LicenceCacheSingleFlight>(),
            sp.GetRequiredService<LicenceCacheTelemetry>(),
            () => DateTime.UtcNow,
            redisSettings.InstanceName);
    }

    private sealed class HostLicenceCatalogueProvider : ICurrentLicenceCatalogueProvider
    {
        private readonly LicenceServiceConfiguration _configuration;

        public HostLicenceCatalogueProvider(LicenceServiceConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public (string Version, string DigestSha256) Current =>
            (_configuration.CatalogueVersion, _configuration.ReleaseDigestSha256);
    }

}
