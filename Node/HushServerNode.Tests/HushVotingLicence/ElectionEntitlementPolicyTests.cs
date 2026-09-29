using FluentAssertions;
using HushNode.Elections;
using HushNode.HushVoting.Licensing.Storage;
using HushShared.Elections.Model;
using HushShared.HushVoting.Licensing.Model;
using Xunit;

namespace HushServerNode.Tests.HushVotingEntitlement;

/// <summary>EPIC-002 -> FEAT-018 AC-002/004 -> P018-3-02 -> T018-3-02.</summary>
public sealed class ElectionEntitlementPolicyTests
{
    [Theory]
    [InlineData("40001", true)]
    [InlineData("40P01", true)]
    [InlineData("23514", false)]
    public void Ef_wrapped_database_conflicts_preserve_whole_transaction_retry_classification(string code, bool retry)
    {
        var postgres = new Npgsql.PostgresException("Fixture database result", "ERROR", "ERROR", code);
        var wrapped = new InvalidOperationException("EF transient wrapper", postgres);
        LicenceDatabaseFailures.IsSerializationConflict(wrapped).Should().Be(retry);
        LicenceDatabaseFailures.IsSerializationConflict(new InvalidOperationException("Application failure")).Should().BeFalse();
    }

    [Theory]
    [InlineData("hushvoting.direct.free", 100)]
    [InlineData("hushvoting.veritas.500", 500)]
    [InlineData("hushvoting.veritas.2000", 2000)]
    [InlineData("hushvoting.veritas.10000", 10000)]
    public void Each_approved_plan_accepts_its_cap_and_rejects_cap_plus_one(string id, int cap)
    {
        var (authority, read) = Fixture(id);
        authority.Check(read, "admin-prod-1of1", ElectionBindingStatus.Binding,
            ElectionGovernanceMode.AdminOnly, cap, out _).Should().Be(ElectionEntitlementReason.None);
        authority.Check(read, "admin-prod-1of1", ElectionBindingStatus.Binding,
            ElectionGovernanceMode.AdminOnly, cap + 1, out _).Should().Be(ElectionEntitlementReason.LimitExceeded);
    }

    [Theory]
    [InlineData("dkg-prod-3of5", ElectionGovernanceMode.TrusteeThreshold)]
    [InlineData("admin-prod-1of1", ElectionGovernanceMode.TrusteeThreshold)]
    [InlineData("admin-dev-1of1", ElectionGovernanceMode.AdminOnly)]
    public void Unlicensed_or_mismatched_governance_never_falls_back_to_a_permitted_profile(string profile, ElectionGovernanceMode mode)
    {
        var (authority, read) = Fixture(HushVotingLicencePlanId.DirectFree.Value);
        authority.Check(read, profile, ElectionBindingStatus.Binding, mode, 1, out var option)
            .Should().Be(ElectionEntitlementReason.ProfileNotAllowed);
        option.Should().BeNull();
    }

    [Fact]
    public void Absence_unavailability_and_unknown_semantics_are_distinct()
    {
        var (authority, read) = Fixture(HushVotingLicencePlanId.DirectFree.Value);
        Check(IndexedEntitlementReadResult.NoActive()).Should().Be(ElectionEntitlementReason.NotActive);
        Check(IndexedEntitlementReadResult.Unavailable("unavailable", "unavailable")).Should().Be(ElectionEntitlementReason.AuthorityUnavailable);
        Check(IndexedEntitlementReadResult.Active(read.Entitlement! with { AssignedCatalogueDigestSha256 = new string('B', 64) }))
            .Should().Be(ElectionEntitlementReason.SemanticsUnsupported);
        ElectionEntitlementReason Check(IndexedEntitlementReadResult value) => authority.Check(value,
            "admin-prod-1of1", ElectionBindingStatus.Binding, ElectionGovernanceMode.AdminOnly, 1, out _);
    }

    private static (ElectionEntitlementAuthorizer, IndexedEntitlementReadResult) Fixture(string id)
    {
        var release = LicenceServiceConfiguration.CreateDefault();
        var plan = release.Catalogue.FindPlan(HushVotingLicencePlanId.TryGetKnown(id)!)!;
        var at = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var snapshot = new EffectiveLicenceEntitlement(Guid.NewGuid(), Guid.NewGuid(), id,
            HushVotingLicenceEnumNames.FamilyToWire(plan.Family).ToLowerInvariant(), plan.UpgradeRank,
            plan.EligibleVoterCap, plan.UnlimitedElections, plan.Term.IsPerpetual ? "perpetual" : "annual", plan.Term.Years,
            plan.GovernanceOptions.Select(o => o.Id.Value).ToArray(), "baseline_free", at,
            plan.Term.IsPerpetual ? null : at.AddYears(plan.Term.Years), release.CatalogueVersion,
            release.ReleaseDigestSha256, 1, Guid.NewGuid());
        return (new(new LicenceCatalogueArchive([release])), IndexedEntitlementReadResult.Active(snapshot));
    }
}
