using FluentAssertions;
using HushNode.HushVoting.Licence.Transactions;
using HushNode.HushVoting.Licensing.Storage;
using HushShared.HushVoting.Licensing.Model;
using Xunit;

namespace HushNode.HushVoting.Licence.gRPC.Tests;

/// <summary>FEAT-018 T018-3-01 / G04: the application boundary preserves indexed terms.</summary>
public sealed class LicencePinnedQueryTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Assigned_terms_are_not_replaced_by_current_catalogue_terms()
    {
        // Deliberately different persisted terms exercise the projection boundary; this is
        // not a claim that an altered release was approved or accepted by the host loader.
        var assigned = Snapshot() with { EligibleVoterCap = 450, UpgradeRank = int.MaxValue,
            UnlimitedElectionPolicy = false, TermYears = 2,
            ExpiresAtUtc = Now.AddYears(2) };
        var service = new LicenceEntitlementQueryApplicationService(
            new Reader(IndexedEntitlementReadResult.Active(assigned)), LicenceServiceConfiguration.CreateDefault(), () => Now);

        var result = await service.GetMyEntitlementAsync("abcdef", CancellationToken.None);

        result.State.Should().Be(HushVotingLicenceEntitlementQueryState.Active);
        result.Active.Should().NotBeNull();
        result.Active!.EligibleVoterCap.Should().Be(assigned.EligibleVoterCap);
        result.Active.UnlimitedElections.Should().BeFalse();
        result.Active.TermYears.Should().Be(2);
        result.Active.HigherOptions.Should().BeEmpty("current options must be ranked against pinned terms");
        result.Active.AllowedGovernanceOptionIds.Should().Equal(assigned.AllowedGovernanceOptionIds);
        result.Active.AssignedCatalogueVersion.Should().Be(assigned.AssignedCatalogueVersion);
        result.Active.LicenceReference.Should().Be(assigned.LicenceReference!.Value.ToString());
    }

    [Fact]
    public async Task Missing_retained_digest_is_unavailable_even_when_the_current_plan_is_known()
    {
        var assigned = Snapshot() with { AssignedCatalogueDigestSha256 = new string('B', 64) };
        var service = new LicenceEntitlementQueryApplicationService(
            new Reader(IndexedEntitlementReadResult.Active(assigned)), LicenceServiceConfiguration.CreateDefault(), () => Now);
        var result = await service.GetMyEntitlementAsync("abcdef", CancellationToken.None);
        result.State.Should().Be(HushVotingLicenceEntitlementQueryState.Unavailable);
        result.Active.Should().BeNull();
        result.DirectFreeTemplate.Should().BeNull();
    }

    [Theory]
    [InlineData("unknown", "hushvoting-licence-catalogue/v1.0.0")]
    [InlineData("hushvoting.veritas.500", "unknown")]
    public async Task Uninterpretable_indexed_semantics_remain_unavailable(string plan, string version)
    {
        var assigned = Snapshot() with { PlanId = plan, AssignedCatalogueVersion = version };
        var service = new LicenceEntitlementQueryApplicationService(
            new Reader(IndexedEntitlementReadResult.Active(assigned)), LicenceServiceConfiguration.CreateDefault(), () => Now);

        var result = await service.GetMyEntitlementAsync("abcdef", CancellationToken.None);

        result.State.Should().Be(HushVotingLicenceEntitlementQueryState.Unavailable);
        result.Active.Should().BeNull();
        result.DirectFreeTemplate.Should().BeNull();
    }

    private static EffectiveLicenceEntitlement Snapshot() => new(
        Guid.NewGuid(), Guid.NewGuid(), HushVotingLicencePlanId.Veritas500.Value, "veritas", 1,
        500, true, "annual", 1, new[] { HushVotingGovernanceOptionId.Trustees3Of5.Value }, "confirmed_upgrade",
        Now.AddDays(-1), Now.AddYears(1), HushVotingLicenceCatalogueVersion.V1Value,
        new string('A', 64), 2, Guid.NewGuid());

    private sealed class Reader(IndexedEntitlementReadResult result) : ILicenceIndexedProjectionReader
    {
        public Task<IndexedEntitlementReadResult> ResolveEffectiveAsync(
            AuthenticatedIdentitySubject subject, DateTime evaluationUtc, CancellationToken cancellationToken) => Task.FromResult(result);
    }
}
