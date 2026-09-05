using FluentAssertions;
using HushNode.HushVoting.Licensing.Cache;
using Xunit;

namespace HushNode.HushVoting.Licensing.Cache.Tests;

/// <summary>Health threshold boundary tests (Task 3.8): thresholds are strict ("exceeds").</summary>
public sealed class LicenceCacheHealthTests
{
    [Fact]
    public void Warning_begins_when_age_or_depth_exceeds_thresholds()
    {
        var options = new LicenceCacheOptions();

        LicenceCacheHealth.EvaluateOutbox(options, 999, TimeSpan.FromMinutes(4))
            .Should().Be(LicenceCacheHealthState.Healthy);
        LicenceCacheHealth.EvaluateOutbox(options, 1_000, TimeSpan.FromMinutes(4))
            .Should().Be(LicenceCacheHealthState.Healthy); // "exceeds 1 000"
        LicenceCacheHealth.EvaluateOutbox(options, 1_001, TimeSpan.FromMinutes(4))
            .Should().Be(LicenceCacheHealthState.Warning);
        LicenceCacheHealth.EvaluateOutbox(options, 500, TimeSpan.FromMinutes(5))
            .Should().Be(LicenceCacheHealthState.Healthy); // "exceeds five minutes"
        LicenceCacheHealth.EvaluateOutbox(options, 500, TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)))
            .Should().Be(LicenceCacheHealthState.Warning);
    }

    [Fact]
    public void Critical_begins_when_age_or_depth_exceeds_critical_thresholds()
    {
        var options = new LicenceCacheOptions();

        LicenceCacheHealth.EvaluateOutbox(options, 10_000, TimeSpan.FromMinutes(1))
            .Should().Be(LicenceCacheHealthState.Warning); // depth 10 000 exceeds warning but not critical
        LicenceCacheHealth.EvaluateOutbox(options, 10_001, TimeSpan.FromMinutes(1))
            .Should().Be(LicenceCacheHealthState.Critical);
        LicenceCacheHealth.EvaluateOutbox(options, 100, TimeSpan.FromHours(1))
            .Should().Be(LicenceCacheHealthState.Warning); // exactly one hour is not "exceeds one hour"
        LicenceCacheHealth.EvaluateOutbox(options, 100, TimeSpan.FromHours(1).Add(TimeSpan.FromSeconds(1)))
            .Should().Be(LicenceCacheHealthState.Critical);
    }
}
