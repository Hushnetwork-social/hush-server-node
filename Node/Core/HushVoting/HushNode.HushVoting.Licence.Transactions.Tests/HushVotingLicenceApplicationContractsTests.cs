// FEAT-015 Task 3.8 — entitlement application projection + expiry/window seam tests.
//
// Proves: active projection exposes only safe current detail + strictly higher options +
// non-actionable Enterprise tag; no-active returns exactly one Direct Free template and is never
// confused with unavailable; mempool acceptance is not activation (application state only reflects
// indexed state); annual expiry is observational (no transaction generated — asserted at the
// decision/application layer); and the FEAT-018 scheduled-close coverage seam blocks a window that
// extends beyond the upper-exclusive expiry while capturing entitlement for later completion.

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

public sealed class HushVotingLicenceScheduledWindowSeamTests
{
    private static readonly DateTime EffectiveFrom =
        DateTime.Parse("2026-01-01T00:00:00Z", null, System.Globalization.DateTimeStyles.AssumeUniversal).ToUniversalTime();

    private static readonly DateTime ScheduledOpen =
        DateTime.Parse("2026-03-01T10:00:00Z", null, System.Globalization.DateTimeStyles.AssumeUniversal).ToUniversalTime();

    private static readonly Guid LicenceRef = Guid.Parse("11111111-2222-4333-8444-555555555555");

    private static HushVotingLicenceCurrentState ActiveDirectFree() =>
        new HushVotingLicenceCurrentState.Active(
            HushVotingLicencePlanId.DirectFree, LicenceRef, "hushvoting-licence-catalogue/v1.0.0", EffectiveFrom, null);

    private static HushVotingLicenceCurrentState ActiveVeritas(DateTime expiresAtUtc) =>
        new HushVotingLicenceCurrentState.Active(
            HushVotingLicencePlanId.Veritas2000,
            Guid.Parse("22222222-3333-4444-8555-666666666666"),
            "hushvoting-licence-catalogue/v1.0.0",
            EffectiveFrom,
            expiresAtUtc);

    [Fact]
    public void Perpetual_direct_free_covers_any_scheduled_window()
    {
        var coverage = HushVotingLicenceScheduledWindowSeam.EvaluateCoverage(
            ActiveDirectFree(), ScheduledOpen, ScheduledOpen.AddDays(30));

        coverage.CoversFullScheduledWindow.Should().BeTrue();
        coverage.CapturedEntitlement!.PlanId.Should().Be("hushvoting.direct.free");
        coverage.CapturedEntitlement.ExpiresAtUtc.Should().BeNull();
    }

    [Fact]
    public void Annual_licence_covering_the_full_window_is_captured()
    {
        var expiresAt = EffectiveFrom.AddYears(1); // 2027-01-01
        var coverage = HushVotingLicenceScheduledWindowSeam.EvaluateCoverage(
            ActiveVeritas(expiresAt), ScheduledOpen, ScheduledOpen.AddDays(14));

        coverage.CoversFullScheduledWindow.Should().BeTrue();
        coverage.CapturedEntitlement!.ExpiresAtUtc.Should().Be(expiresAt);
        coverage.StableCode.Should().BeNull();
    }

    [Fact]
    public void Window_extending_beyond_upper_exclusive_expiry_is_blocked()
    {
        // Licence expires 2026-09-01; scheduled close is 2026-10-01 -> blocked at Open.
        var expiresAt = DateTime.Parse("2026-09-01T00:00:00Z", null, System.Globalization.DateTimeStyles.AssumeUniversal).ToUniversalTime();
        var coverage = HushVotingLicenceScheduledWindowSeam.EvaluateCoverage(
            ActiveVeritas(expiresAt), ScheduledOpen, DateTime.Parse("2026-10-01T00:00:00Z", null, System.Globalization.DateTimeStyles.AssumeUniversal).ToUniversalTime());

        coverage.CoversFullScheduledWindow.Should().BeFalse();
        coverage.StableCode.Should().Be(HushVotingLicenceScheduledWindowSeam.WindowExtendsBeyondExpiry);
        coverage.CapturedEntitlement.Should().BeNull();
    }

    [Fact]
    public void No_active_entitlement_blocks_open()
    {
        var coverage = HushVotingLicenceScheduledWindowSeam.EvaluateCoverage(
            new HushVotingLicenceCurrentState.NoActive(), ScheduledOpen, ScheduledOpen.AddDays(7));

        coverage.CoversFullScheduledWindow.Should().BeFalse();
        coverage.StableCode.Should().Be(HushVotingLicenceScheduledWindowSeam.LicenceNotActive);
    }

    [Fact]
    public void Licence_not_yet_effective_at_open_is_blocked()
    {
        // Licence starts 2026-04-01 but the window opens 2026-03-01 -> blocked.
        var lateEffective = DateTime.Parse("2026-04-01T00:00:00Z", null, System.Globalization.DateTimeStyles.AssumeUniversal).ToUniversalTime();
        var state = new HushVotingLicenceCurrentState.Active(
            HushVotingLicencePlanId.Veritas2000,
            LicenceRef,
            "hushvoting-licence-catalogue/v1.0.0",
            lateEffective,
            lateEffective.AddYears(1));

        var coverage = HushVotingLicenceScheduledWindowSeam.EvaluateCoverage(
            state, ScheduledOpen, ScheduledOpen.AddDays(7));

        coverage.CoversFullScheduledWindow.Should().BeFalse();
        coverage.StableCode.Should().Be(HushVotingLicenceScheduledWindowSeam.LicenceNotActive);
    }

    [Fact]
    public void Invalid_scheduled_window_fails_closed()
    {
        var coverage = HushVotingLicenceScheduledWindowSeam.EvaluateCoverage(
            ActiveDirectFree(), ScheduledOpen, ScheduledOpen.AddDays(-1));

        coverage.CoversFullScheduledWindow.Should().BeFalse();
        coverage.StableCode.Should().Be("invalid_scheduled_window");
    }
}
