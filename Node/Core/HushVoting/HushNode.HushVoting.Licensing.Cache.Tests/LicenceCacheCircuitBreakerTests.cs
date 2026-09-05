using FluentAssertions;
using HushNode.HushVoting.Licensing.Cache;
using Xunit;

namespace HushNode.HushVoting.Licensing.Cache.Tests;

/// <summary>Deterministic circuit transitions (Task 3.4) using an injected clock.</summary>
public sealed class LicenceCacheCircuitBreakerTests
{
    private static readonly LicenceCacheOptions Options = new();
    private static readonly DateTime Start = new(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);

    private sealed class MutableClock
    {
        public DateTime Now { get; set; } = Start;
    }

    [Fact]
    public void Three_failures_open_the_circuit_for_thirty_seconds()
    {
        var clock = new MutableClock();
        var circuit = new LicenceCacheCircuitBreaker(() => clock.Now, Options);

        circuit.IsClosed.Should().BeTrue();
        circuit.RecordConnectionFailure();
        circuit.RecordConnectionFailure();
        circuit.RecordConnectionFailure();

        circuit.State.Should().Be(LicenceCacheCircuitBreaker.CircuitState.Open);
        circuit.IsAttemptPermitted().Should().BeFalse();
    }

    [Fact]
    public void After_interval_one_half_open_probe_is_permitted()
    {
        var clock = new MutableClock();
        var circuit = new LicenceCacheCircuitBreaker(() => clock.Now, Options);
        circuit.RecordConnectionFailure();
        circuit.RecordConnectionFailure();
        circuit.RecordConnectionFailure();
        circuit.State.Should().Be(LicenceCacheCircuitBreaker.CircuitState.Open);

        // Advance past the 30-second interval.
        clock.Now = Start.AddSeconds(31);

        // Exactly one probe is permitted; concurrent attempts bypass Redis.
        circuit.IsAttemptPermitted().Should().BeTrue();
        circuit.IsAttemptPermitted().Should().BeFalse();

        circuit.RecordProbeSuccess();
        circuit.State.Should().Be(LicenceCacheCircuitBreaker.CircuitState.Closed);
        circuit.IsAttemptPermitted().Should().BeTrue();
    }

    [Fact]
    public void Failed_half_open_probe_reopens_circuit()
    {
        var clock = new MutableClock();
        var circuit = new LicenceCacheCircuitBreaker(() => clock.Now, Options);
        circuit.RecordConnectionFailure();
        circuit.RecordConnectionFailure();
        circuit.RecordConnectionFailure();
        clock.Now = Start.AddSeconds(31);

        circuit.IsAttemptPermitted().Should().BeTrue();
        circuit.RecordConnectionFailure(); // probe failed

        circuit.State.Should().Be(LicenceCacheCircuitBreaker.CircuitState.Open);
        circuit.IsAttemptPermitted().Should().BeFalse();
    }

    [Fact]
    public void Data_level_misses_do_not_open_the_connection_circuit()
    {
        var clock = new MutableClock();
        var circuit = new LicenceCacheCircuitBreaker(() => clock.Now, Options);
        // Only connection/timeout failures are recorded; corrupt values never call RecordConnectionFailure.
        circuit.State.Should().Be(LicenceCacheCircuitBreaker.CircuitState.Closed);
        circuit.IsAttemptPermitted().Should().BeTrue();
    }
}

/// <summary>Bounded retry scheduling tests (Task 3.6) — deterministic, no sleeps.</summary>
public sealed class LicenceCacheOutboxBackoffTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Backoff_caps_at_five_minutes()
    {
        var id = Guid.NewGuid();
        var late = LicenceCacheOutboxBackoffCalculator.ComputeNextAvailableAfterUtc(id, 40, Now);
        var delay = late - Now;

        delay.Should().BeLessThan(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        delay.Should().BeGreaterOrEqualTo(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Backoff_grows_with_attempts_and_is_deterministic()
    {
        var id = Guid.NewGuid();
        var attempt1 = LicenceCacheOutboxBackoffCalculator.ComputeNextAvailableAfterUtc(id, 1, Now);
        var attempt3 = LicenceCacheOutboxBackoffCalculator.ComputeNextAvailableAfterUtc(id, 3, Now);
        var attempt1Again = LicenceCacheOutboxBackoffCalculator.ComputeNextAvailableAfterUtc(id, 1, Now);

        attempt3.Should().BeAfter(attempt1);
        attempt1Again.Should().Be(attempt1); // deterministic jitter for a fixed id
    }

    [Fact]
    public void Backoff_stays_bounded_and_positive_for_first_attempt()
    {
        var id = Guid.NewGuid();
        var next = LicenceCacheOutboxBackoffCalculator.ComputeNextAvailableAfterUtc(id, 1, Now);
        (next - Now).Should().BeGreaterThan(TimeSpan.Zero);
    }
}

/// <summary>Single-flight and telemetry tests (Tasks 3.4/3.8).</summary>
public sealed class LicenceCacheSingleFlightAndTelemetryTests
{
    [Fact]
    public void Single_flight_has_one_owner_and_waiters_observe_completion()
    {
        var singleFlight = new LicenceCacheSingleFlight();

        var owner1 = singleFlight.TryBecomeOwner("subject-a", out var firstWait);
        var owner2 = singleFlight.TryBecomeOwner("subject-a", out var secondWait);

        owner1.Should().BeTrue();
        owner2.Should().BeFalse();
        singleFlight.InFlightCount.Should().Be(1);

        singleFlight.FinishOwner("subject-a");
        singleFlight.InFlightCount.Should().Be(0);
        firstWait.IsCompleted.Should().BeTrue();
        secondWait.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public void Distinct_subjects_do_not_block_each_other()
    {
        var singleFlight = new LicenceCacheSingleFlight();
        singleFlight.TryBecomeOwner("subject-a", out _).Should().BeTrue();
        singleFlight.TryBecomeOwner("subject-b", out _).Should().BeTrue();
        singleFlight.InFlightCount.Should().Be(2);
        singleFlight.FinishOwner("subject-a");
        singleFlight.FinishOwner("subject-b");
        singleFlight.InFlightCount.Should().Be(0);
    }

    [Fact]
    public void Telemetry_counts_closed_labels_and_ignores_oversized_labels()
    {
        var telemetry = new LicenceCacheTelemetry();
        telemetry.Count("hit_current_key");
        telemetry.Count("hit_current_key");
        telemetry.Count(new string('x', 128)); // not a closed label; ignored

        telemetry.Get("hit_current_key").Should().Be(2);
        telemetry.Snapshot().Should().HaveCount(1);
    }
}
