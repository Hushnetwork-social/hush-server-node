using FluentAssertions;
using HushNode.HushVoting.Licensing.Cache;
using Xunit;

namespace HushNode.HushVoting.Licensing.Cache.Tests;

/// <summary>
/// Contract tests for the non-authoritative cached projection: a closed, safe display shape with no
/// raw address, database key, authentication metadata, history, or transport surface.
/// </summary>
public sealed class CachedEntitlementProjectionContractTests
{
    [Fact]
    public void Projection_exposes_only_client_safe_fields_and_revision()
    {
        var allowed = new CachedEntitlementProjection(
            planId: "hushvoting.veritas.500",
            planFamily: "Veritas",
            upgradeRank: 2,
            eligibleVoterCap: 500,
            unlimitedElectionPolicy: false,
            termKind: "annual",
            termYears: 1,
            allowedGovernanceOptionIds: new[] { "g1" },
            expiresAtUtc: new DateTime(2027, 9, 5, 0, 0, 0, DateTimeKind.Utc),
            entitlementRevision: 8);

        var names = typeof(CachedEntitlementProjection).GetProperties()
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToArray();

        names.Should().BeEquivalentTo(new[]
        {
            nameof(CachedEntitlementProjection.PlanId),
            nameof(CachedEntitlementProjection.PlanFamily),
            nameof(CachedEntitlementProjection.UpgradeRank),
            nameof(CachedEntitlementProjection.EligibleVoterCap),
            nameof(CachedEntitlementProjection.UnlimitedElectionPolicy),
            nameof(CachedEntitlementProjection.TermKind),
            nameof(CachedEntitlementProjection.TermYears),
            nameof(CachedEntitlementProjection.AllowedGovernanceOptionIds),
            nameof(CachedEntitlementProjection.ExpiresAtUtc),
            nameof(CachedEntitlementProjection.EntitlementRevision),
        });

        allowed.PlanId.Should().Be("hushvoting.veritas.500");
        allowed.EntitlementRevision.Should().Be(8);
    }

    [Fact]
    public void Projection_cannot_convert_to_authoritative_entitlement()
    {
        // The authoritative FEAT-013 entitlement type must not be reachable from the projection:
        // there is no explicit or implicit conversion operator and no inheritance relationship.
        var projectionType = typeof(CachedEntitlementProjection);
        var authoritative = typeof(HushNode.HushVoting.Licensing.Storage.EffectiveLicenceEntitlement);

        authoritative.IsAssignableFrom(projectionType).Should().BeFalse();
        projectionType.IsAssignableFrom(authoritative).Should().BeFalse();
        projectionType.GetMethods().Where(m => m.Name is "op_Implicit" or "op_Explicit")
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "family", 0, 0, "kind", 1, 0)]          // empty plan id
    [InlineData("hushvoting.direct.free", null, 0, 0, "kind", 1, 0)] // null plan family
    [InlineData("hushvoting.direct.free", "Direct", -1, 0, "kind", 1, 0)] // negative rank
    [InlineData("hushvoting.direct.free", "Direct", 0, -1, "kind", 1, 0)] // negative cap
    [InlineData("hushvoting.direct.free", "Direct", 0, 0, "", 1, 0)]  // empty term kind
    [InlineData("hushvoting.direct.free", "Direct", 0, 0, "kind", 0, 0)] // non-positive years
    [InlineData("hushvoting.direct.free", "Direct", 0, 0, "kind", 1, -1)] // negative revision
    public void Projection_rejects_invalid_inputs(
        string planId,
        string? planFamily,
        int upgradeRank,
        int? cap,
        string termKind,
        int termYears,
        long revision)
    {
        var act = () => new CachedEntitlementProjection(
            planId,
            planFamily!,
            upgradeRank,
            cap,
            unlimitedElectionPolicy: false,
            termKind,
            termYears,
            Array.Empty<string>(),
            expiresAtUtc: null,
            entitlementRevision: revision);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Projection_rejects_oversized_governance_option_list()
    {
        var tooMany = Enumerable.Range(0, CachedEntitlementProjection.MaxAllowedGovernanceOptionIds + 1)
            .Select(i => $"g{i}")
            .ToArray();

        var act = () => new CachedEntitlementProjection(
            "hushvoting.direct.free",
            "Direct",
            0,
            null,
            unlimitedElectionPolicy: true,
            "perpetual",
            1,
            tooMany,
            expiresAtUtc: null,
            entitlementRevision: 1);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Result_success_carries_projection_and_source_outcome()
    {
        var projection = new CachedEntitlementProjection(
            "hushvoting.direct.free", "Direct", 0, null, true, "perpetual", 1,
            Array.Empty<string>(), null, 1);

        var hit = CachedEntitlementReadResult.Success(EntitlementCacheReadOutcome.CacheHit, projection);
        hit.IsSuccess.Should().BeTrue();
        hit.Outcome.Should().Be(EntitlementCacheReadOutcome.CacheHit);
        hit.Projection.Should().BeSameAs(projection);
        hit.StableErrorCode.Should().BeNull();
    }

    [Fact]
    public void Result_failure_never_carries_projection()
    {
        var failure = CachedEntitlementReadResult.Failure(
            EntitlementCacheReadOutcome.AuthorityUnavailable,
            "authority_unavailable",
            "authority unavailable");

        failure.IsSuccess.Should().BeFalse();
        failure.Projection.Should().BeNull();
        failure.StableErrorCode.Should().Be("authority_unavailable");
    }
}
