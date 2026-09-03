using FluentAssertions;
using HushShared.HushVoting.Licensing.Model;
using Xunit;

namespace HushServerNode.Tests.HushVotingLicence;

public sealed class HushVotingLicenceCatalogueValidatorTests
{
    private static HushVotingLicenceCatalogue Canonical() => HushVotingLicenceCatalogueV1.CreateCatalogue();

    private static IReadOnlyList<HushVotingLicencePlan> ClonePlans(HushVotingLicenceCatalogue catalogue) =>
        catalogue.Plans.Select(static p => new HushVotingLicencePlan(
            p.Id,
            p.Family,
            p.DisplayName,
            p.SafeDescription,
            p.DisplayOrder,
            p.UpgradeRank,
            p.EligibleVoterCap,
            p.UnlimitedElections,
            p.Term,
            p.Availability,
            p.UnavailableSafeReason,
            p.GovernanceOptions,
            p.CatalogueVersion)).ToArray();

    [Fact]
    public void ExactV1Candidate_ValidatesAndBuildsSnapshot()
    {
        var catalogue = Canonical();
        var result = HushVotingLicenceCatalogueValidator.ValidateAndBuild(
            ClonePlans(catalogue),
            HushVotingProfileCompatibilityV1.Entries);

        result.IsValid.Should().BeTrue();
        result.Catalogue.Should().NotBeNull();
        result.Catalogue!.Version.Should().Be(HushVotingLicenceCatalogueVersion.V1);
    }

    [Fact]
    public void MissingPlan_ReturnsPlanSetInvalid()
    {
        var catalogue = Canonical();
        var plans = ClonePlans(catalogue).Where(p => p.Id != HushVotingLicencePlanId.Enterprise).ToArray();

        var result = HushVotingLicenceCatalogueValidator.ValidateAndBuild(
            plans,
            HushVotingProfileCompatibilityV1.Entries);

        result.IsValid.Should().BeFalse();
        result.Catalogue.Should().BeNull();
        result.Validation.Failures.Should().Contain(f =>
            f.Code == HushVotingLicenceValidationCodes.LicCatPlanSetInvalid &&
            f.Message.Contains("hushvoting.enterprise", StringComparison.Ordinal));
    }

    [Fact]
    public void PaidDirectPlaceholder_NeverEscapesConstruction()
    {
        // A forbidden unverified paid Direct tier id is not a known closed value, so the aggregate
        // constructor rejects it: no partial/placeholder plan can ever enter a catalogue.
        var act = () => new HushVotingLicencePlan(
            HushVotingLicencePlanId.FromExternal("hushvoting.direct.500"),
            HushVotingLicenceFamily.Direct,
            "HushVoting! Direct 500",
            "Unverified paid Direct placeholder",
            60,
            500,
            500,
            true,
            HushVotingLicenceTerm.OneCalendarYear,
            HushVotingLicenceAvailability.Unavailable,
            null,
            Array.Empty<HushVotingGovernanceOption>(),
            HushVotingLicenceCatalogueVersion.V1);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*known closed value*");
    }

    [Fact]
    public void AlteredRankCapTermAvailabilityGovernance_AccumulateAllErrors()
    {
        var catalogue = Canonical();
        var plans = ClonePlans(catalogue).ToList();
        var veritas2000 = plans.Single(p => p.Id == HushVotingLicencePlanId.Veritas2000);

        plans[plans.IndexOf(veritas2000)] = new HushVotingLicencePlan(
            veritas2000.Id,
            veritas2000.Family,
            veritas2000.DisplayName,
            veritas2000.SafeDescription,
            veritas2000.DisplayOrder,
            upgradeRank: 1500, // wrong rank
            eligibleVoterCap: 3000, // wrong cap
            veritas2000.UnlimitedElections,
            HushVotingLicenceTerm.Perpetual, // wrong term
            HushVotingLicenceAvailability.Unavailable, // wrong availability
            "no reason",
            veritas2000.GovernanceOptions, // intact
            veritas2000.CatalogueVersion);

        var result = HushVotingLicenceCatalogueValidator.ValidateAndBuild(
            plans,
            HushVotingProfileCompatibilityV1.Entries);

        result.IsValid.Should().BeFalse();
        var codes = result.Validation.Failures.Select(static f => f.Code).Distinct().ToArray();
        codes.Should().Contain(HushVotingLicenceValidationCodes.LicCatRankInvalid);
        codes.Should().Contain(HushVotingLicenceValidationCodes.LicCatLimitInvalid);
        codes.Should().Contain(HushVotingLicenceValidationCodes.LicCatTermInvalid);
        codes.Should().Contain(HushVotingLicenceValidationCodes.LicCatDefaultInvalid); // availability drift
        result.Validation.Failures.Count.Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void GovernanceOptionSetDrift_ReturnsGovernanceInvalid()
    {
        var catalogue = Canonical();
        var plans = ClonePlans(catalogue).ToList();
        var veritas500 = plans.Single(p => p.Id == HushVotingLicencePlanId.Veritas500);

        plans[plans.IndexOf(veritas500)] = new HushVotingLicencePlan(
            veritas500.Id,
            veritas500.Family,
            veritas500.DisplayName,
            veritas500.SafeDescription,
            veritas500.DisplayOrder,
            veritas500.UpgradeRank,
            veritas500.EligibleVoterCap,
            veritas500.UnlimitedElections,
            veritas500.Term,
            veritas500.Availability,
            veritas500.UnavailableSafeReason,
            governanceOptions: veritas500.GovernanceOptions
                .Concat(new[]
                {
                    new HushVotingGovernanceOption(
                        HushVotingGovernanceOptionId.Trustees7Of10,
                        10,
                        7,
                        "7 of 10 trustees",
                        new HashSet<HushVotingBindingStatus> { HushVotingBindingStatus.Binding }),
                })
                .ToArray(),
            veritas500.CatalogueVersion);

        var result = HushVotingLicenceCatalogueValidator.ValidateAndBuild(
            plans,
            HushVotingProfileCompatibilityV1.Entries);

        result.IsValid.Should().BeFalse();
        result.Validation.Failures.Should().Contain(f =>
            f.Code == HushVotingLicenceValidationCodes.LicCatGovernanceInvalid &&
            f.Message.Contains("must be exactly", StringComparison.Ordinal));
    }

    [Fact]
    public void UnsafeCopy_ReturnsCopyUnsafe()
    {
        var catalogue = Canonical();
        var plans = ClonePlans(catalogue).ToList();
        var directFree = plans.Single(p => p.Id == HushVotingLicencePlanId.DirectFree);

        plans[plans.IndexOf(directFree)] = new HushVotingLicencePlan(
            directFree.Id,
            directFree.Family,
            directFree.DisplayName,
            safeDescription: "Elections for up to 100 voters. Contact sales for price information.",
            directFree.DisplayOrder,
            directFree.UpgradeRank,
            directFree.EligibleVoterCap,
            directFree.UnlimitedElections,
            directFree.Term,
            directFree.Availability,
            directFree.UnavailableSafeReason,
            directFree.GovernanceOptions,
            directFree.CatalogueVersion);

        var result = HushVotingLicenceCatalogueValidator.ValidateAndBuild(
            plans,
            HushVotingProfileCompatibilityV1.Entries);

        result.IsValid.Should().BeFalse();
        result.Validation.Failures.Should().Contain(f =>
            f.Code == HushVotingLicenceValidationCodes.LicCatCopyUnsafe);
    }

    [Fact]
    public void MissingRequiredMapping_ReturnsProfileMissing()
    {
        var catalogue = Canonical();
        var mappings = HushVotingProfileCompatibilityV1.Entries
            .Where(e => !(e.GovernanceOptionId == HushVotingGovernanceOptionId.Trustees8Of13 &&
                          e.BindingStatus == HushVotingBindingStatus.Binding))
            .ToArray();

        var result = HushVotingLicenceCatalogueValidator.ValidateAndBuild(ClonePlans(catalogue), mappings);

        result.IsValid.Should().BeFalse();
        result.Validation.Failures.Should().Contain(f =>
            f.Code == HushVotingLicenceValidationCodes.LicCatProfileMissing);
    }

    [Fact]
    public void WrongMapping_ReturnsProfileMismatch()
    {
        var catalogue = Canonical();
        var mappings = HushVotingProfileCompatibilityV1.Entries
            .Select(e => e.GovernanceOptionId == HushVotingGovernanceOptionId.Trustees3Of5 &&
                         e.BindingStatus == HushVotingBindingStatus.Binding
                ? e with { RuntimeProfileId = "dkg-prod-7of10" }
                : e)
            .ToArray();

        var result = HushVotingLicenceCatalogueValidator.ValidateAndBuild(ClonePlans(catalogue), mappings);

        result.IsValid.Should().BeFalse();
        result.Validation.Failures.Should().Contain(f =>
            f.Code == HushVotingLicenceValidationCodes.LicCatProfileMismatch);
    }

    [Fact]
    public void ErrorsAreOrderedDeterministicallyByCodeThenPath()
    {
        var catalogue = Canonical();
        var plans = ClonePlans(catalogue).ToList();
        var plan = plans[1];
        plans[1] = new HushVotingLicencePlan(
            plan.Id,
            plan.Family,
            plan.DisplayName,
            "unsafe copy with price and internal note",
            plan.DisplayOrder,
            plan.UpgradeRank + 1,
            plan.EligibleVoterCap,
            plan.UnlimitedElections,
            HushVotingLicenceTerm.Perpetual,
            plan.Availability,
            plan.UnavailableSafeReason,
            plan.GovernanceOptions,
            plan.CatalogueVersion);

        var result = HushVotingLicenceCatalogueValidator.ValidateAndBuild(
            plans,
            HushVotingProfileCompatibilityV1.Entries);

        var ordered = result.Validation.Failures
            .Zip(result.Validation.Failures.Skip(1), static (a, b) => a.CompareTo(b) <= 0)
            .All(static x => x);
        ordered.Should().BeTrue();
    }
}

public sealed class HushVotingLicenceUpgradeEvaluatorTests
{
    private readonly HushVotingLicenceCatalogue _catalogue = HushVotingLicenceCatalogueV1.CreateCatalogue();

    private void AssertAllowed(string currentValue, string targetValue)
    {
        var result = HushVotingLicenceUpgradeEvaluator.Evaluate(
            _catalogue,
            HushVotingLicencePlanId.FromExternal(currentValue),
            HushVotingLicencePlanId.FromExternal(targetValue));

        result.Allowed.Should().BeTrue($"{currentValue} -> {targetValue} should be allowed");
    }

    private void AssertRejected(string currentValue, string targetValue)
    {
        var result = HushVotingLicenceUpgradeEvaluator.Evaluate(
            _catalogue,
            HushVotingLicencePlanId.FromExternal(currentValue),
            HushVotingLicencePlanId.FromExternal(targetValue));

        result.Allowed.Should().BeFalse($"{currentValue} -> {targetValue} should be rejected");
        result.StableCode.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("hushvoting.direct.free", "hushvoting.veritas.500")]
    [InlineData("hushvoting.direct.free", "hushvoting.veritas.2000")]
    [InlineData("hushvoting.direct.free", "hushvoting.veritas.10000")]
    [InlineData("hushvoting.veritas.500", "hushvoting.veritas.2000")]
    [InlineData("hushvoting.veritas.500", "hushvoting.veritas.10000")]
    [InlineData("hushvoting.veritas.2000", "hushvoting.veritas.10000")]
    public void ExactlySixHigherTransitions_AreAllowed(string current, string target) => AssertAllowed(current, target);

    [Theory]
    [InlineData("hushvoting.direct.free", "hushvoting.direct.free")]
    [InlineData("hushvoting.veritas.500", "hushvoting.veritas.500")]
    [InlineData("hushvoting.veritas.10000", "hushvoting.veritas.500")]
    [InlineData("hushvoting.veritas.2000", "hushvoting.direct.free")]
    [InlineData("hushvoting.veritas.10000", "hushvoting.veritas.2000")]
    [InlineData("hushvoting.direct.free", "hushvoting.enterprise")]
    [InlineData("hushvoting.veritas.500", "hushvoting.enterprise")]
    [InlineData("hushvoting.veritas.10000", "hushvoting.enterprise")]
    [InlineData("hushvoting.direct.free", "unknown.plan")]
    [InlineData("unknown.current", "hushvoting.veritas.500")]
    public void NonActionableTransitions_AreRejected(string current, string target) => AssertRejected(current, target);

    [Fact]
    public void AllowedTransition_IsPureAndNonMutating()
    {
        var before = _catalogue.Plans.Count;
        var result = HushVotingLicenceUpgradeEvaluator.Evaluate(
            _catalogue,
            HushVotingLicencePlanId.DirectFree,
            HushVotingLicencePlanId.Veritas2000);

        result.Allowed.Should().BeTrue();
        _catalogue.Plans.Count.Should().Be(before);
    }
}

public sealed class HushVotingGovernanceLockEvaluatorTests
{
    private readonly HushVotingLicencePlan _veritas10k =
        HushVotingLicenceCatalogueV1.CreateCatalogue().FindPlan(HushVotingLicencePlanId.Veritas10000)!;

    [Fact]
    public void DraftWithoutArtifact_AllowsAuthorizedChange()
    {
        var result = HushVotingGovernanceLockEvaluator.Evaluate(
            _veritas10k,
            HushVotingGovernanceOptionId.Trustees7Of10,
            isOpen: false,
            hasCeremonyArtifact: false);

        result.Allowed.Should().BeTrue();
    }

    [Fact]
    public void FirstCeremonyArtifact_LocksTheChoice()
    {
        var result = HushVotingGovernanceLockEvaluator.Evaluate(
            _veritas10k,
            HushVotingGovernanceOptionId.Trustees8Of13,
            isOpen: false,
            hasCeremonyArtifact: true);

        result.Allowed.Should().BeFalse();
        result.StableCode.Should().Be(HushVotingGovernanceLockEvaluator.GovernanceLockedByArtifact);
    }

    [Fact]
    public void Open_AlwaysLocksEvenWithoutArtifact()
    {
        var result = HushVotingGovernanceLockEvaluator.Evaluate(
            _veritas10k,
            HushVotingGovernanceOptionId.Trustees3Of5,
            isOpen: true,
            hasCeremonyArtifact: false);

        result.Allowed.Should().BeFalse();
        result.StableCode.Should().Be(HushVotingGovernanceLockEvaluator.GovernanceLockedByOpen);
    }

    [Fact]
    public void OptionNotAuthorizedByPlan_IsRejected()
    {
        var directFree = HushVotingLicenceCatalogueV1.CreateCatalogue()
            .FindPlan(HushVotingLicencePlanId.DirectFree)!;

        var result = HushVotingGovernanceLockEvaluator.Evaluate(
            directFree,
            HushVotingGovernanceOptionId.Trustees3Of5,
            isOpen: false,
            hasCeremonyArtifact: false);

        result.Allowed.Should().BeFalse();
        result.StableCode.Should().Be(HushVotingGovernanceLockEvaluator.GovernanceOptionNotAuthorized);
    }

    [Fact]
    public void NoMutationInstruction_IsProducedOnRejection()
    {
        var result = HushVotingGovernanceLockEvaluator.Evaluate(
            _veritas10k,
            HushVotingGovernanceOptionId.Trustees7Of10,
            isOpen: false,
            hasCeremonyArtifact: true);

        result.Allowed.Should().BeFalse();
        result.StableCode.Should().Be(HushVotingGovernanceLockEvaluator.GovernanceLockedByArtifact);
        // The pure decision returns only a stable code and a reason: no mutation command surface.
        result.GetType().GetProperties().Select(static p => p.Name).Should().NotContain(
            new[] { "Rewrite", "Delete", "Mutation" });
    }
}

public sealed class HushVotingLicenceProjectionAndCompatibilityTests
{
    private readonly HushVotingLicenceCatalogue _catalogue = HushVotingLicenceCatalogueV1.CreateCatalogue();

    [Fact]
    public void Projection_ContainsOnlySafeFields()
    {
        var plan = _catalogue.FindPlan(HushVotingLicencePlanId.Veritas10000)!;
        var projection = HushVotingLicencePlanProjectionMapper.ToProjection(plan);

        projection.PlanId.Should().Be(plan.Id);
        projection.EligibleVoterCap.Should().Be(10000);
        projection.GovernanceOptions.Select(static o => o.Id.Value).Should().Contain("trustees-8of13");

        var json = System.Text.Json.JsonSerializer.Serialize(projection);
        json.Should().NotContain("RuntimeProfileId");
        json.Should().NotContain("admin-");
        json.Should().NotContain("dkg-");
        json.Should().NotContain("digest");
    }

    [Fact]
    public void ZeroTrustees_ResolvesToAdminProfiles_ButProjectsZeroZero()
    {
        var resolution = HushVotingLicenceProfileCompatibilityResolver.Resolve(
            _catalogue,
            HushVotingLicencePlanId.Veritas500,
            HushVotingGovernanceOptionId.NoCustomerTrustees,
            HushVotingBindingStatus.Binding);

        resolution.IsResolved.Should().BeTrue();
        resolution.RuntimeProfileId.Should().Be("admin-prod-1of1");
        resolution.CustomerTrusteeCount.Should().Be(0);
        resolution.CustomerRequiredApprovalCount.Should().Be(0);
    }

    [Fact]
    public void FixedTrustee_ResolvesToExactDkgProfile()
    {
        var resolution = HushVotingLicenceProfileCompatibilityResolver.Resolve(
            _catalogue,
            HushVotingLicencePlanId.Veritas2000,
            HushVotingGovernanceOptionId.Trustees7Of10,
            HushVotingBindingStatus.NonBinding);

        resolution.IsResolved.Should().BeTrue();
        resolution.RuntimeProfileId.Should().Be("dkg-dev-7of10");
        resolution.CustomerTrusteeCount.Should().Be(10);
        resolution.DevOnly.Should().BeTrue();
    }

    [Fact]
    public void Enterprise_HasNoExecutableMapping()
    {
        var resolution = HushVotingLicenceProfileCompatibilityResolver.Resolve(
            _catalogue,
            HushVotingLicencePlanId.Enterprise,
            HushVotingGovernanceOptionId.Trustees3Of5,
            HushVotingBindingStatus.Binding);

        resolution.IsResolved.Should().BeFalse();
        resolution.StableCode.Should().Contain("ENTERPRISE");
    }

    [Fact]
    public void PlanNotAuthorizingOption_IsUnresolved()
    {
        var resolution = HushVotingLicenceProfileCompatibilityResolver.Resolve(
            _catalogue,
            HushVotingLicencePlanId.DirectFree,
            HushVotingGovernanceOptionId.Trustees8Of13,
            HushVotingBindingStatus.NonBinding);

        resolution.IsResolved.Should().BeFalse();
        resolution.StableCode.Should().Contain("NOT_AUTHORIZED");
    }
}
