using FluentAssertions;
using HushNode.HushVoting.Licensing.Cache;
using Xunit;

namespace HushNode.HushVoting.Licensing.Cache.Tests;

/// <summary>
/// Absolute TTL/expiry boundary tests (Task 2.5/2.6): deterministic 0-10% reduction from the subject
/// digest, seven-day maximum, annual assignment-expiry cap, conservative rounding, and
/// non-positive-lifetime handling. Pure function of deterministic inputs; no wall-clock sleeps.
/// </summary>
public sealed class LicenceCacheTtlCalculatorTests
{
    private static readonly LicenceCacheOptions Options = new();
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    private static byte[] Digest(byte firstByte) =>
        Enumerable.Range(0, 32).Select(i => i == 0 ? firstByte : (byte)(i + 1)).ToArray();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void Jitter_reduction_stays_within_zero_to_ten_percent(byte digestByte)
    {
        var result = LicenceCacheTtlCalculator.Compute(Digest(digestByte), Now, null, Options);

        result.HasPositiveLifetime.Should().BeTrue();
        var full = TimeSpan.FromDays(Options.MaxTtlDays);
        var remaining = result.CacheValidUntilUtc - Now;

        remaining.Should().BeGreaterThan(TimeSpan.Zero);
        remaining.Should().BeLessOrEqualTo(full);
        remaining.Should().BeGreaterOrEqualTo(full - TimeSpan.FromTicks(full.Ticks * Options.MaxTtlJitterPercent / 100L));

        // Conservative rounding: Redis TTL seconds never outlive the computed boundary.
        (Now + TimeSpan.FromSeconds(result.RedisTtlSeconds) <= result.CacheValidUntilUtc)
            .Should().BeTrue();
    }

    [Fact]
    public void Max_ttl_is_exactly_seven_days_for_zero_jitter_subject()
    {
        var result = LicenceCacheTtlCalculator.Compute(Digest(0), Now, null, Options);
        (result.CacheValidUntilUtc - Now).Should().Be(TimeSpan.FromDays(7));
        result.RedisTtlSeconds.Should().Be(7 * 24 * 60 * 60);
    }

    [Fact]
    public void Annual_assignment_expiry_caps_validity()
    {
        var expiry = Now.AddDays(3);
        var result = LicenceCacheTtlCalculator.Compute(Digest(0), Now, expiry, Options);

        result.CacheValidUntilUtc.Should().Be(expiry); // capped at upper-exclusive expiry
        result.RedisTtlSeconds.Should().Be(3 * 24 * 60 * 60);
    }

    [Fact]
    public void Expiry_in_the_past_yields_no_positive_lifetime()
    {
        var result = LicenceCacheTtlCalculator.Compute(Digest(0), Now, Now, Options);
        result.HasPositiveLifetime.Should().BeFalse();
        result.RedisTtlSeconds.Should().Be(0);

        var past = LicenceCacheTtlCalculator.Compute(Digest(0), Now, Now.AddMinutes(-1), Options);
        past.HasPositiveLifetime.Should().BeFalse();
        past.RedisTtlSeconds.Should().Be(0);
    }

    [Fact]
    public void Digest_determinism_yields_identical_boundaries()
    {
        var a = LicenceCacheTtlCalculator.Compute(Digest(7), Now, null, Options);
        var b = LicenceCacheTtlCalculator.Compute(Digest(7), Now, null, Options);
        a.Should().BeEquivalentTo(b);
    }

    [Fact]
    public void Sub_second_boundary_rounds_conservatively()
    {
        // Assignment expiry 0.4 seconds from now: whole-second TTL must be 0 (never outlive expiry).
        var expiry = Now.AddMilliseconds(400);
        var result = LicenceCacheTtlCalculator.Compute(Digest(0), Now, expiry, Options);
        result.HasPositiveLifetime.Should().BeFalse();
        result.RedisTtlSeconds.Should().Be(0);
    }

    [Fact]
    public void Invalid_digest_length_is_rejected()
    {
        var act = () => LicenceCacheTtlCalculator.Compute(new byte[16], Now, null, Options);
        act.Should().Throw<ArgumentException>();
    }
}
