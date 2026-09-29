// FEAT-015 Task 3.8 application projection; FEAT-018 T018-3-03 actual-Open interval policy.
//
// Proves: active projection exposes only safe current detail + strictly higher options +
// non-actionable Enterprise tag; no-active returns exactly one Direct Free template and is never
// confused with unavailable; mempool acceptance is not activation (application state only reflects
// indexed state); annual expiry is observational (no transaction generated — asserted at the
// decision/application layer). DD018-06 replaces the earlier full-window assumption: actual
// Open must be within the interval; later Close is independent. Lifecycle Twins prove persistence.

using FluentAssertions;
using HushShared.HushVoting.Licensing.Model;
using Xunit;

namespace HushNode.HushVoting.Licence.Transactions.Tests;

public sealed class HushVotingLicenceEntitlementApplicationProjectorTests
{
    private static readonly HushVotingLicenceCatalogue Catalogue = HushVotingLicenceCatalogueV1.CreateCatalogue();

    private static readonly DateTime EffectiveFrom =
        DateTime.Parse("2026-01-01T00:00:00Z", null, System.Globalization.DateTimeStyles.AssumeUniversal).ToUniversalTime();

    private static readonly Guid LicenceRef = Guid.Parse("11111111-2222-4333-8444-555555555555");

    [Fact]
    public void No_active_projects_exactly_one_direct_free_template()
    {
        var result = HushVotingLicenceEntitlementApplicationProjector.Project(
            Catalogue, new HushVotingLicenceCurrentState.NoActive());

        result.State.Should().Be(HushVotingLicenceEntitlementQueryState.NoActive);
        result.DirectFreeTemplate.Should().NotBeNull();
        result.DirectFreeTemplate!.TransitionIntent.Should().Be(HushVotingLicenceTransitionIntent.BaselineFree);
        result.DirectFreeTemplate.RequestedPlanId.Should().Be("hushvoting.direct.free");
        result.Active.Should().BeNull();
        result.StableErrorCode.Should().BeNull();
    }

    [Fact]
    public void Active_direct_free_projects_safe_detail_and_higher_options_only()
    {
        var state = new HushVotingLicenceCurrentState.Active(
            HushVotingLicencePlanId.DirectFree, LicenceRef, Catalogue.Version.Value, EffectiveFrom, null);

        var result = HushVotingLicenceEntitlementApplicationProjector.Project(Catalogue, state);

        result.State.Should().Be(HushVotingLicenceEntitlementQueryState.Active);
        var view = result.Active!;
        view.LicenceReference.Should().Be(LicenceRef.ToString());
        view.PlanId.Should().Be("hushvoting.direct.free");
        view.EligibleVoterCap.Should().Be(100);
        view.ExpiresAtUtc.Should().BeNull();

        // Strictly higher, currently available Veritas options only (no lower plans).
        view.HigherOptions.Select(o => o.PlanId).Should().Equal(
            "hushvoting.veritas.500", "hushvoting.veritas.2000", "hushvoting.veritas.10000");
        view.HigherOptions.Should().NotContain(o => o.PlanId == "hushvoting.direct.free");
    }

    [Fact]
    public void Active_veritas_projects_only_strictly_higher_options()
    {
        var state = new HushVotingLicenceCurrentState.Active(
            HushVotingLicencePlanId.Veritas2000,
            Guid.Parse("22222222-3333-4444-8555-666666666666"),
            Catalogue.Version.Value,
            EffectiveFrom,
            EffectiveFrom.AddYears(1));

        var result = HushVotingLicenceEntitlementApplicationProjector.Project(Catalogue, state);

        result.Active!.HigherOptions.Select(o => o.PlanId).Should().Equal("hushvoting.veritas.10000");
        result.Active!.HigherOptions.Select(o => o.PlanId).Should().NotContain(
            "hushvoting.direct.free", "hushvoting.veritas.500", "hushvoting.veritas.2000");
    }

    [Fact]
    public void Active_view_includes_non_actionable_enterprise_entry()
    {
        var state = new HushVotingLicenceCurrentState.Active(
            HushVotingLicencePlanId.DirectFree, LicenceRef, Catalogue.Version.Value, EffectiveFrom, null);

        var view = HushVotingLicenceEntitlementApplicationProjector.Project(Catalogue, state).Active!;

        view.Enterprise.Should().NotBeNull();
        view.Enterprise!.PlanId.Should().Be("hushvoting.enterprise");
    }

    [Fact]
    public void Mempool_pending_is_never_surfaced_as_activation()
    {
        // The application projector only ever consumes indexed state; pending mempool entries are
        // invisible by construction. A no-active indexed state stays no-active even if a transaction
        // were pending — proven here by the absence of any pending input surface.
        var result = HushVotingLicenceEntitlementApplicationProjector.Project(
            Catalogue, new HushVotingLicenceCurrentState.NoActive());

        result.State.Should().Be(HushVotingLicenceEntitlementQueryState.NoActive);
        result.Active.Should().BeNull();
    }

    [Fact]
    public void Unavailable_state_is_never_no_active_or_direct_free()
    {
        // Unknown current state cannot be constructed, but the projector fails closed by refusing to
        // fabricate Direct Free from any non-active, non-no-active input shape.
        var result = HushVotingLicenceEntitlementApplicationProjector.Project(
            Catalogue, new HushVotingLicenceCurrentState.Active(
                HushVotingLicencePlanId.FromExternal("hushvoting.unknown.plan"),
                LicenceRef,
                Catalogue.Version.Value,
                EffectiveFrom,
                null));

        result.State.Should().Be(HushVotingLicenceEntitlementQueryState.Unavailable);
        result.DirectFreeTemplate.Should().BeNull();
    }
}

// FEAT-018 DD018-06 explicitly supersedes FEAT-015's provisional full-window assumption.
public sealed class HushVotingLicenceActualOpenInstantTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void Effective_start_is_inclusive(int seconds, bool allowed) =>
        HushVotingLicenceOpenInstantPolicy.IsEffectiveAt(Start, Start.AddYears(1), Start.AddSeconds(seconds)).Should().Be(allowed);

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void Actual_open_expiry_is_exclusive(int seconds, bool allowed) =>
        HushVotingLicenceOpenInstantPolicy.IsEffectiveAt(Start, Start.AddYears(1), Start.AddYears(1).AddSeconds(seconds)).Should().Be(allowed);

    [Fact]
    public void Perpetual_rights_have_no_artificial_upper_boundary() =>
        HushVotingLicenceOpenInstantPolicy.IsEffectiveAt(Start, null, Start.AddYears(20)).Should().BeTrue();

    [Fact]
    public void Invalid_clock_or_interval_is_not_authority()
    {
        HushVotingLicenceOpenInstantPolicy.IsEffectiveAt(Start, Start, Start).Should().BeFalse();
        HushVotingLicenceOpenInstantPolicy.IsEffectiveAt(Start, Start.AddSeconds(-1), Start).Should().BeFalse();
        HushVotingLicenceOpenInstantPolicy.IsEffectiveAt(Start, null, DateTime.SpecifyKind(Start, DateTimeKind.Unspecified)).Should().BeFalse();
        HushVotingLicenceOpenInstantPolicy.IsEffectiveAt(DateTime.SpecifyKind(Start, DateTimeKind.Local), null, Start).Should().BeFalse();
    }
}
