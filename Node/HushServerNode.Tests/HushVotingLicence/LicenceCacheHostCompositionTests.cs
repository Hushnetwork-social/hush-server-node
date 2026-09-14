using FluentAssertions;
using HushNode.HushVoting.Licensing.Cache;
using HushServerNode.HushVotingLicensingIntegration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HushServerNode.Tests.HushVotingLicence;

/// <summary>
/// FEAT-014 Phase 6 host-composition and readiness tests (Task 6.2). These are deterministic DI
/// assertions that require no external Redis/PostgreSQL process: composition consumes the existing
/// multiplexer registration (never adds a second owner) and the readiness matrix follows the exact
/// enabled/disabled/degraded/configuration-failed rules.
/// </summary>
public sealed class LicenceCacheHostCompositionTests
{
    private const string Section = "HushVotingLicenceCache";

    // Public 32-byte test-only master key material (never used outside tests).
    private const string TestCurrentSecret = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    private const string TestPreviousSecret = "QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVowMTIzNDU2";

    private static IConfiguration EnabledConfig() =>
        JsonConfig(
            "{\"Enabled\": true," +
            "\"Current\": {\"KeyId\": \"current-v1\", \"SecretBase64\": \"" + TestCurrentSecret + "\", \"RotationStartedUtc\": \"2026-09-01T00:00:00Z\"}," +
            "\"Previous\": {\"KeyId\": \"previous-v0\", \"SecretBase64\": \"" + TestPreviousSecret + "\", \"RotationStartedUtc\": \"2026-08-20T00:00:00Z\"}}");

    private static IConfiguration DisabledConfig() => JsonConfig("{\"Enabled\": false}");

    private static IConfiguration JsonConfig(string inner) =>
        new ConfigurationBuilder().AddJsonStream(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"" + Section + "\": " + inner + "}"))).Build();

    // ------------------------------------------------------------------ duplicate Redis capability prohibited

    [Fact]
    public void Cache_composition_does_not_add_a_second_redis_connection_owner()
    {
        var services = new ServiceCollection();

        // The FEAT-014 composition must never register a multiplexer or connection-manager owner.
        HushVotingLicenceCacheHostBuild.AddHushVotingLicenceCacheServices(services, EnabledConfig());

        services.Should().NotContain(d =>
            d.ServiceType == typeof(StackExchange.Redis.IConnectionMultiplexer));
        services.Should().NotContain(d =>
            d.ServiceType == typeof(HushNode.Notifications.RedisConnectionManager));
        services.Should().ContainSingle(d => d.ServiceType == typeof(ICachedEntitlementReader));
    }

    // ------------------------------------------------------------------ readiness mode matrix

    [Theory]
    [InlineData(false, null, false, LicenceCacheRuntimeMode.Disabled)]
    [InlineData(false, "cache_keyring_missing_current", true, LicenceCacheRuntimeMode.Disabled)]
    [InlineData(true, null, true, LicenceCacheRuntimeMode.Ready)]
    [InlineData(true, null, false, LicenceCacheRuntimeMode.Degraded)]
    [InlineData(true, "cache_options_invalid_max_ttl", true, LicenceCacheRuntimeMode.ConfigurationFailed)]
    public void Readiness_matrix_follows_exact_rules(
        bool enabled,
        string? securityError,
        bool redisUsable,
        LicenceCacheRuntimeMode expected)
    {
        var result = LicenceCacheRuntimeStatus.Evaluate(enabled, securityError, redisUsable);

        result.Mode.Should().Be(expected);
        result.NodeAvailable.Should().Be(expected != LicenceCacheRuntimeMode.ConfigurationFailed);
    }

    [Fact]
    public void Intentionally_disabled_cache_registers_no_redis_reader()
    {
        var services = new ServiceCollection();
        HushVotingLicenceCacheHostBuild.AddHushVotingLicenceCacheServices(services, DisabledConfig());

        services.Should().NotContain(d => d.ServiceType == typeof(ICachedEntitlementReader));
        services.Should().NotContain(d => d.ServiceType == typeof(IEntitlementProjectionStore));
    }

    [Fact]
    public void Enabled_cache_with_valid_keys_registers_the_composed_reader_set()
    {
        var services = new ServiceCollection();
        HushVotingLicenceCacheHostBuild.AddHushVotingLicenceCacheServices(services, EnabledConfig());

        services.Should().ContainSingle(d => d.ServiceType == typeof(LicenceCacheOptions));
        services.Should().ContainSingle(d => d.ServiceType == typeof(LicenceCacheKeyRing));
        services.Should().ContainSingle(d => d.ServiceType == typeof(ICachedEntitlementReader));
        services.Should().ContainSingle(d => d.ServiceType == typeof(IEntitlementProjectionStore));
        services.Should().ContainSingle(d => d.ServiceType == typeof(LicenceCacheTelemetry));
    }
}
