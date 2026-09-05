using FluentAssertions;
using HushNode.HushVoting.Licensing.Cache;
using Xunit;

namespace HushNode.HushVoting.Licensing.Cache.Tests;

/// <summary>Validation tests for the non-secret licence-cache options vocabulary (Task 2.1).</summary>
public sealed class LicenceCacheOptionsTests
{
    [Fact]
    public void Defaults_are_valid_and_match_frozen_v1_bounds()
    {
        var options = new LicenceCacheOptions();

        options.Validate().Should().BeNull();
        options.MaxTtlDays.Should().Be(7);
        options.MaxTtlJitterPercent.Should().Be(10);
        options.PreviousKeyOverlapMaxDays.Should().Be(14);
        options.FillLeaseSeconds.Should().Be(5);
        options.WaiterPollBudgetMs.Should().Be(750);
        options.CircuitOpenFailureCount.Should().Be(3);
        options.CircuitOpenSeconds.Should().Be(30);
        options.MaxEnvelopeBytes.Should().Be(16 * 1024);
        options.DeliveredRetentionDays.Should().Be(30);
        options.OutboxHealthWarningOldestAge.Should().Be(TimeSpan.FromMinutes(5));
        options.OutboxHealthWarningDepth.Should().Be(1_000);
        options.OutboxHealthCriticalOldestAge.Should().Be(TimeSpan.FromHours(1));
        options.OutboxHealthCriticalDepth.Should().Be(10_000);
    }

    [Fact]
    public void Invalid_max_ttl_is_rejected()
    {
        var options = new LicenceCacheOptions { MaxTtlDays = 0 };
        options.Validate().Should().Be(LicenceCacheOptionErrorCodes.InvalidMaxTtl);
    }

    [Fact]
    public void Invalid_jitter_is_rejected()
    {
        var options = new LicenceCacheOptions { MaxTtlJitterPercent = 11 };
        options.Validate().Should().Be(LicenceCacheOptionErrorCodes.InvalidJitter);
    }

    [Fact]
    public void Invalid_lease_and_waiter_bounds_are_rejected()
    {
        new LicenceCacheOptions { FillLeaseSeconds = 0 }.Validate()
            .Should().Be(LicenceCacheOptionErrorCodes.InvalidLeaseSeconds);

        new LicenceCacheOptions { WaiterPollBudgetMs = 10_000 }.Validate()
            .Should().Be(LicenceCacheOptionErrorCodes.InvalidWaiterBudget);
    }

    [Fact]
    public void Invalid_circuit_bounds_are_rejected()
    {
        new LicenceCacheOptions { CircuitOpenFailureCount = 0 }.Validate()
            .Should().Be(LicenceCacheOptionErrorCodes.InvalidCircuitThreshold);

        new LicenceCacheOptions { CircuitOpenSeconds = 0 }.Validate()
            .Should().Be(LicenceCacheOptionErrorCodes.InvalidCircuitOpenSeconds);
    }

    [Fact]
    public void Invalid_envelope_and_retention_bounds_are_rejected()
    {
        new LicenceCacheOptions { MaxEnvelopeBytes = 128 }.Validate()
            .Should().Be(LicenceCacheOptionErrorCodes.InvalidMaxEnvelopeBytes);

        new LicenceCacheOptions { DeliveredRetentionDays = 0 }.Validate()
            .Should().Be(LicenceCacheOptionErrorCodes.InvalidRetentionDays);
    }

    [Fact]
    public void Invalid_health_thresholds_are_rejected()
    {
        new LicenceCacheOptions { OutboxHealthWarningDepth = 0 }.Validate()
            .Should().Be(LicenceCacheOptionErrorCodes.InvalidHealthThreshold);

        new LicenceCacheOptions
        {
            OutboxHealthCriticalOldestAge = TimeSpan.FromMinutes(1),
        }.Validate().Should().Be(LicenceCacheOptionErrorCodes.InvalidHealthThreshold);
    }
}
