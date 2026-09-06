// FEAT-015 Task 3.3/3.4 — transition decision + composite validator tests.
//
// Proves the pure transition matrix (baseline only when no-active; upgrade with exact current
// reference and strictly higher available Veritas target) and the composite validator sequencing
// (kind → shape → size → real signature → identity → catalogue → state → decision). Every expected
// rejection is typed with its exact stable LICENCE_* code.

using FluentAssertions;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.HushVoting.Licensing.Model;
using Xunit;

namespace HushNode.HushVoting.Licence.Transactions.Tests;

public sealed class HushVotingLicenceTransitionMatrixTests
{
    private static readonly HushVotingLicenceCatalogue Catalogue = HushVotingLicenceCatalogueV1.CreateCatalogue();
    private static readonly Guid BaselineTx = HushVotingLicenceTestData.BaselineTransactionId;
    private static readonly Guid DirectFreeCurrentTx = Guid.Parse("11111111-2222-4333-8444-555555555555");

    private static readonly DateTime EffectiveFrom =
        DateTime.Parse("2026-01-01T00:00:00Z", null, System.Globalization.DateTimeStyles.AssumeUniversal).ToUniversalTime();

    private static HushVotingLicenceCurrentState NoActive() => new HushVotingLicenceCurrentState.NoActive();

    private static HushVotingLicenceCurrentState ActiveDirectFree() =>
        new HushVotingLicenceCurrentState.Active(
            HushVotingLicencePlanId.DirectFree,
            DirectFreeCurrentTx,
            HushVotingLicenceCatalogueVersion.V1Value,
            EffectiveFrom,
            null);

    private static HushVotingLicenceCurrentState ActiveVeritas500() =>
        new HushVotingLicenceCurrentState.Active(
            HushVotingLicencePlanId.Veritas500,
            Guid.Parse("22222222-3333-4444-8555-666666666666"),
            HushVotingLicenceCatalogueVersion.V1Value,
            EffectiveFrom,
            EffectiveFrom.AddYears(1));

    private static HushVotingLicenceCurrentState ActiveVeritas2000() =>
        new HushVotingLicenceCurrentState.Active(
            HushVotingLicencePlanId.Veritas2000,
            Guid.Parse("33333333-4444-4555-8666-777777777777"),
            HushVotingLicenceCatalogueVersion.V1Value,
            EffectiveFrom,
            EffectiveFrom.AddYears(1));

    private static HushVotingLicenceAssignmentPayload Baseline(string planId = HushVotingLicenceTestData.DirectFree) =>
        new(HushVotingLicenceTransitionIntent.BaselineFree, planId, HushVotingLicenceCatalogueVersion.V1Value);

    private static HushVotingLicenceAssignmentPayload Upgrade(
        string targetPlanId,
        Guid expectedCurrentTx,
        string expectedCurrentPlanId) =>
        new(
            HushVotingLicenceTransitionIntent.ConfirmedUpgrade,
            targetPlanId,
            HushVotingLicenceCatalogueVersion.V1Value,
            expectedCurrentTx,
            expectedCurrentPlanId);

    private static HushVotingLicenceTransitionDecision Decide(HushVotingLicenceAssignmentPayload payload, HushVotingLicenceCurrentState state) =>
        HushVotingLicenceTransitionDecisionCore.Decide(Catalogue, payload, state);

    // ------------------------------------------------------------------ baseline

    [Fact]
    public void Baseline_direct_free_with_no_active_is_allowed()
    {
        var decision = Decide(Baseline(), NoActive());

        decision.IsValid.Should().BeTrue();
        decision.OperativeFacts!.PlanId.Should().Be(HushVotingLicencePlanId.DirectFree);
        decision.OperativeFacts.Term.IsPerpetual.Should().BeTrue();
        decision.OperativeFacts.EligibleVoterCap.Should().Be(100);
        decision.OperativeFacts.TransitionIntent.Should().Be(HushVotingLicenceTransitionIntent.BaselineFree);
    }

    [Fact]
    public void Baseline_while_any_active_entitlement_exists_is_rejected()
    {
        var decision = Decide(Baseline(), ActiveDirectFree());

        decision.IsValid.Should().BeFalse();
        decision.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.BaselineRequiresNoActiveEntitlement);
    }

    [Fact]
    public void Baseline_targeting_veritas_is_rejected_as_not_higher()
    {
        var decision = Decide(Baseline(HushVotingLicenceTestData.Veritas2000), NoActive());

        decision.IsValid.Should().BeFalse();
        decision.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.TransitionNotHigher);
    }

    [Fact]
    public void Baseline_targeting_enterprise_is_rejected_as_admin_only()
    {
        var decision = Decide(Baseline("hushvoting.enterprise"), NoActive());

        decision.IsValid.Should().BeFalse();
        decision.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.EnterpriseAdminOnly);
    }

    [Fact]
    public void Unknown_target_plan_is_rejected_as_plan_unknown()
    {
        var decision = Decide(Baseline("hushvoting.veritas.999"), NoActive());

        decision.IsValid.Should().BeFalse();
        decision.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.PlanUnknown);
    }

    // ------------------------------------------------------------------ upgrade

    [Fact]
    public void Upgrade_from_direct_free_to_veritas2000_with_exact_reference_is_allowed()
    {
        var decision = Decide(
            Upgrade(HushVotingLicenceTestData.Veritas2000, DirectFreeCurrentTx, HushVotingLicenceTestData.DirectFree),
            ActiveDirectFree());

        decision.IsValid.Should().BeTrue();
        decision.OperativeFacts!.PlanId.Should().Be(HushVotingLicencePlanId.Veritas2000);
        decision.OperativeFacts.UpgradeRank.Should().BeGreaterThan(0);
        decision.OperativeFacts.Term.IsOneCalendarYear.Should().BeTrue();
        decision.OperativeFacts.EligibleVoterCap.Should().Be(2000);
        decision.OperativeFacts.TransitionIntent.Should().Be(HushVotingLicenceTransitionIntent.ConfirmedUpgrade);
    }

    [Fact]
    public void Tier_skipping_from_direct_free_to_veritas10000_is_allowed()
    {
        var decision = Decide(
            Upgrade(HushVotingLicenceTestData.Veritas10000, DirectFreeCurrentTx, HushVotingLicenceTestData.DirectFree),
            ActiveDirectFree());

        decision.IsValid.Should().BeTrue();
        decision.OperativeFacts!.EligibleVoterCap.Should().Be(10000);
    }

    [Fact]
    public void Upgrade_without_active_entitlement_is_rejected()
    {
        var decision = Decide(
            Upgrade(HushVotingLicenceTestData.Veritas2000, DirectFreeCurrentTx, HushVotingLicenceTestData.DirectFree),
            NoActive());

        decision.IsValid.Should().BeFalse();
        decision.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.UpgradeRequiresActiveEntitlement);
    }

    [Fact]
    public void Upgrade_with_stale_current_reference_is_rejected()
    {
        // Expected current tx differs from indexed truth.
        var decision = Decide(
            Upgrade(
                HushVotingLicenceTestData.Veritas2000,
                Guid.Parse("99999999-0000-4111-8222-333333333333"),
                HushVotingLicenceTestData.DirectFree),
            ActiveDirectFree());

        decision.IsValid.Should().BeFalse();
        decision.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.ExpectedCurrentInvalid);
    }

    [Fact]
    public void Upgrade_with_wrong_expected_current_plan_is_rejected()
    {
        var decision = Decide(
            Upgrade(HushVotingLicenceTestData.Veritas2000, DirectFreeCurrentTx, HushVotingLicenceTestData.Veritas500),
            ActiveDirectFree());

        decision.IsValid.Should().BeFalse();
        decision.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.ExpectedCurrentInvalid);
    }

    [Fact]
    public void Upgrade_to_current_plan_is_rejected_as_unchanged()
    {
        var decision = Decide(
            Upgrade(HushVotingLicenceTestData.Veritas500, Guid.Parse("22222222-3333-4444-8555-666666666666"), HushVotingLicenceTestData.Veritas500),
            ActiveVeritas500());

        decision.IsValid.Should().BeFalse();
        decision.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.TransitionUnchanged);
    }

    [Fact]
    public void Upgrade_to_lower_plan_is_rejected_as_not_higher()
    {
        // Veritas 2000 -> Direct Free downgrade.
        var decision = Decide(
            Upgrade(HushVotingLicenceTestData.DirectFree, Guid.Parse("33333333-4444-4555-8666-777777777777"), HushVotingLicenceTestData.Veritas2000),
            ActiveVeritas2000());

        decision.IsValid.Should().BeFalse();
        decision.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.TransitionNotHigher);
    }

    [Fact]
    public void Upgrade_to_enterprise_is_rejected_as_admin_only()
    {
        var decision = Decide(
            Upgrade("hushvoting.enterprise", DirectFreeCurrentTx, HushVotingLicenceTestData.DirectFree),
            ActiveDirectFree());

        decision.IsValid.Should().BeFalse();
        decision.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.EnterpriseAdminOnly);
    }

    // ------------------------------------------------------------------ current-state derived semantics

    [Fact]
    public void Upgrade_operative_facts_are_server_derived_from_the_catalogue()
    {
        var decision = Decide(
            Upgrade(HushVotingLicenceTestData.Veritas2000, DirectFreeCurrentTx, HushVotingLicenceTestData.DirectFree),
            ActiveDirectFree());

        var facts = decision.OperativeFacts!;
        facts.PlanFamily.Should().Be("veritas");
        facts.GovernanceOptionIds.Should().NotBeEmpty();
        facts.TermYears.Should().Be(1);
        facts.AssignedCatalogueVersion.Should().Be(HushVotingLicenceCatalogueVersion.V1Value);
    }
}

public sealed class HushVotingLicenceCompositeValidatorTests
{
    private static readonly DateTime CompositeEffectiveFrom =
        DateTime.Parse("2026-01-01T00:00:00Z", null, System.Globalization.DateTimeStyles.AssumeUniversal).ToUniversalTime();

    private readonly HushVotingLicenceCanonicalSerializer _serializer = new();
    private readonly HushVotingLicenceSignatureVerifier _verifier = new();
    private readonly FakeContextSource _context = new();

    private HushVotingLicenceTransactionValidator BuildValidator() =>
        new(_serializer, _verifier, _context);

    private sealed class FakeContextSource : IHushVotingLicenceValidationContextSource
    {
        public HushVotingLicenceCurrentState State { get; set; } = new HushVotingLicenceCurrentState.NoActive();
        public bool IdentityExists { get; set; } = true;

        public Task<HushVotingLicenceCatalogue> GetCurrentCatalogueAsync(CancellationToken cancellationToken) =>
            Task.FromResult(HushVotingLicenceCatalogueV1.CreateCatalogue());

        public Task<HushVotingLicenceSignatoryContext?> ResolveIdentityAsync(
            string canonicalPublicSigningAddress,
            CancellationToken cancellationToken) =>
            Task.FromResult<HushVotingLicenceSignatoryContext?>(
                IdentityExists
                    ? new HushVotingLicenceSignatoryContext(canonicalPublicSigningAddress, 42)
                    : null);

        public Task<HushVotingLicenceCurrentState> ResolveCurrentStateAsync(
            HushVotingLicenceSignatoryContext identity,
            CancellationToken cancellationToken) =>
            Task.FromResult(State);
    }

    [Fact]
    public async Task Valid_baseline_transaction_passes_composite_validation()
    {
        _context.State = new HushVotingLicenceCurrentState.NoActive();
        var tx = HushVotingLicenceTestData.BuildSigned(
            new HushVotingLicenceAssignmentPayload(
                HushVotingLicenceTransitionIntent.BaselineFree,
                HushVotingLicenceTestData.DirectFree,
                HushVotingLicenceCatalogueVersion.V1Value));

        var result = await BuildValidator().ValidateAsync(tx, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.ValidatedContent.Should().BeOfType<HushVotingLicenceTransitionDecision>();
    }

    [Fact]
    public async Task Valid_upgrade_transaction_passes_composite_validation()
    {
        var currentTx = Guid.Parse("11111111-2222-4333-8444-555555555555");
        _context.State = new HushVotingLicenceCurrentState.Active(
            HushVotingLicencePlanId.DirectFree,
            currentTx,
            HushVotingLicenceCatalogueVersion.V1Value,
            CompositeEffectiveFrom,
            null);
        var tx = HushVotingLicenceTestData.BuildSigned(
            new HushVotingLicenceAssignmentPayload(
                HushVotingLicenceTransitionIntent.ConfirmedUpgrade,
                HushVotingLicenceTestData.Veritas2000,
                HushVotingLicenceCatalogueVersion.V1Value,
                currentTx,
                HushVotingLicenceTestData.DirectFree),
            HushVotingLicenceTestData.UpgradeTransactionId);

        var result = await BuildValidator().ValidateAsync(tx, CancellationToken.None);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Missing_identity_is_rejected_before_transition_semantics()
    {
        _context.IdentityExists = false;
        var tx = HushVotingLicenceTestData.BuildSigned();

        var result = await BuildValidator().ValidateAsync(tx, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.SignatoryIdentityNotFound);
    }

    [Fact]
    public async Task Stale_catalogue_is_rejected()
    {
        // The payload observes a release that is not the current immutable V1 catalogue.
        var tx = HushVotingLicenceTestData.BuildSigned(
            new HushVotingLicenceAssignmentPayload(
                HushVotingLicenceTransitionIntent.BaselineFree,
                HushVotingLicenceTestData.DirectFree,
                "hushvoting-licence-catalogue/v0.9.0"));

        var result = await BuildValidator().ValidateAsync(tx, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.CatalogueStale);
    }

    [Fact]
    public async Task Recorded_size_mismatch_is_rejected()
    {
        var tx = HushVotingLicenceTestData.BuildSigned(payloadSizeOverride: 9999);

        var result = await BuildValidator().ValidateAsync(tx, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.PayloadSizeMismatch);
    }

    [Fact]
    public async Task Wrong_payload_kind_is_rejected()
    {
        var tx = HushVotingLicenceTestData.BuildSigned();
        var wrongKind = tx with { PayloadKind = Guid.NewGuid() };

        var result = await BuildValidator().ValidateAsync(wrongKind, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.PayloadKindUnsupported);
    }

    [Fact]
    public async Task CanValidate_matches_only_the_licence_payload_kind()
    {
        var validator = BuildValidator();

        validator.CanValidate(HushVotingLicenceAssignmentPayloadHandler.LicenceAssignmentPayloadKind).Should().BeTrue();
        validator.CanValidate(Guid.NewGuid()).Should().BeFalse();
    }
}
