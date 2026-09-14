using FluentAssertions;
using HushNode.HushVoting.Licensing.Cache;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HushNode.HushVoting.Licensing.Cache.Tests;

/// <summary>
/// Deterministic host-composition readiness tests (Phase 6 Tasks 6.1/6.2). Evaluates the exact
/// enabled/disabled/degraded/configuration-failed matrix and proves node availability follows the
/// rule that only an invalid enabled security configuration fails node readiness.
/// </summary>
public sealed class LicenceCacheRuntimeStatusTests
{
    [Theory]
    [InlineData(false, null, false, LicenceCacheRuntimeMode.Disabled, true)]
    [InlineData(false, null, true, LicenceCacheRuntimeMode.Disabled, true)]
    [InlineData(false, "cache_keyring_missing_current", true, LicenceCacheRuntimeMode.Disabled, true)]
    [InlineData(true, null, true, LicenceCacheRuntimeMode.Ready, true)]
    [InlineData(true, null, false, LicenceCacheRuntimeMode.Degraded, true)]
    [InlineData(true, "cache_options_invalid_max_ttl", true, LicenceCacheRuntimeMode.ConfigurationFailed, false)]
    [InlineData(true, "cache_keyring_duplicate_key_id", true, LicenceCacheRuntimeMode.ConfigurationFailed, false)]
    [InlineData(true, "cache_keyring_overlap_exceeds_limit", true, LicenceCacheRuntimeMode.ConfigurationFailed, false)]
    public void Evaluate_matrix_matches_specification(
        bool enabled,
        string? securityError,
        bool redisUsable,
        LicenceCacheRuntimeMode expectedMode,
        bool expectedNodeAvailable)
    {
        var result = LicenceCacheRuntimeStatus.Evaluate(enabled, securityError, redisUsable);

        result.Mode.Should().Be(expectedMode);
        result.NodeAvailable.Should().Be(expectedNodeAvailable);
        result.StableCode.Should().Be(expectedMode == LicenceCacheRuntimeMode.ConfigurationFailed ? securityError : null);
    }
}

/// <summary>
/// Deterministic outbox worker lifecycle tests (Phase 6 Task 6.2): start/cancel/stop without any
/// Redis or PostgreSQL process, using an injected clock and a counting dispatcher double.
/// </summary>
public sealed class LicenceCacheOutboxWorkerTests
{
    private sealed class CountingDispatcher : ILicenceCacheOutboxDispatcher
    {
        public int DispatchCalls { get; private set; }
        public int PurgeCalls { get; private set; }

        public Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
        {
            DispatchCalls++;
            return Task.FromResult(0);
        }

        public Task<int> PurgeDeliveredOnceAsync(CancellationToken cancellationToken)
        {
            PurgeCalls++;
            return Task.FromResult(0);
        }
    }

    [Fact]
    public async Task Worker_starts_stops_and_recovers_without_external_process()
    {
        var dispatcher = new CountingDispatcher();
        var now = new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
        DateTime current = now;

        using var worker = new LicenceCacheOutboxWorker(
            dispatcher,
            NullLogger<LicenceCacheOutboxWorker>.Instance,
            utcNow: () => current,
            dispatchInterval: () => TimeSpan.FromSeconds(1),
            purgeInterval: () => TimeSpan.FromHours(1));

        await worker.StartAsync(CancellationToken.None);

        // Run one dispatch pass deterministically (no wall-clock dependency). The background loop may
        // also have executed a pass; only the stop contract is asserted deterministically here.
        var dispatched = await worker.RunDispatchOnceAsync(CancellationToken.None);
        dispatched.Should().Be(0);
        dispatcher.DispatchCalls.Should().BeGreaterThanOrEqualTo(1);

        await worker.StopAsync(CancellationToken.None);
        var callsAtStop = dispatcher.DispatchCalls;
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        dispatcher.DispatchCalls.Should().Be(callsAtStop); // loop stopped
    }

    [Fact]
    public async Task RunPurge_once_invokes_dispatcher_purge()
    {
        var dispatcher = new CountingDispatcher();
        using var worker = new LicenceCacheOutboxWorker(
            dispatcher,
            NullLogger<LicenceCacheOutboxWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        var purged = await worker.RunPurgeOnceAsync(CancellationToken.None);
        purged.Should().Be(0);
        dispatcher.PurgeCalls.Should().Be(1);

        await worker.StopAsync(CancellationToken.None);
    }
}
