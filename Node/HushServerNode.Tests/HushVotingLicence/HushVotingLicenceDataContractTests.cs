using FluentAssertions;
using HushShared.HushVoting.Licensing.Model;
using Xunit;

namespace HushServerNode.Tests.HushVotingLicence;

public sealed class HushVotingLicenceCatalogueV1Tests
{
    private readonly HushVotingLicenceCatalogue _catalogue = HushVotingLicenceCatalogueV1.CreateCatalogue();

    [Fact]
    public void V1Catalogue_HasExactVersionAndFivePlansInDisplayOrder()
    {
        _catalogue.Version.Should().Be(HushVotingLicenceCatalogueVersion.V1);
        _catalogue.Plans.Select(static p => p.Id.Value).Should().Equal(
            HushVotingLicencePlanId.DirectFreeValue,
            HushVotingLicencePlanId.Veritas500Value,
            HushVotingLicencePlanId.Veritas2000Value,
            HushVotingLicencePlanId.Veritas10000Value,
            HushVotingLicencePlanId.EnterpriseValue);
    }

    [Fact]
    public void DirectFree_HasExactV1Semantics()
    {
        var plan = _catalogue.FindPlan(HushVotingLicencePlanId.DirectFree)!;

        plan.Family.Should().Be(HushVotingLicenceFamily.Direct);
        plan.DisplayName.Should().Be("HushVoting! Direct Free");
        plan.DisplayOrder.Should().Be(10);
        plan.UpgradeRank.Should().Be(0);
        plan.EligibleVoterCap.Should().Be(100);
        plan.UnlimitedElections.Should().BeTrue();
        plan.Term.Should().Be(HushVotingLicenceTerm.Perpetual);
        plan.Availability.Should().Be(HushVotingLicenceAvailability.Default);
        plan.SafeDescription.Should().Contain("no customer trustees");
    }

    [Theory]
    [InlineData(HushVotingLicencePlanId.Veritas500Value, "HushVoting! Veritas 500", 20, 1000, 500)]
    [InlineData(HushVotingLicencePlanId.Veritas2000Value, "HushVoting! Veritas 2k", 30, 2000, 2000)]
    [InlineData(HushVotingLicencePlanId.Veritas10000Value, "HushVoting! Veritas 10k", 40, 3000, 10000)]
    public void VeritasPlans_HaveExactOrderRankCapAndAnnualTerm(string idValue, string displayName, int order, int rank, int cap)
    {
        var plan = _catalogue.FindPlan(HushVotingLicencePlanId.FromExternal(idValue))!;

        plan.Family.Should().Be(HushVotingLicenceFamily.Veritas);
        plan.DisplayName.Should().Be(displayName);
        plan.DisplayOrder.Should().Be(order);
        plan.UpgradeRank.Should().Be(rank);
        plan.EligibleVoterCap.Should().Be(cap);
        plan.UnlimitedElections.Should().BeTrue();
        plan.Term.IsOneCalendarYear.Should().BeTrue();
        plan.Term.SafeDescription.Should().Be("One calendar year");
        plan.Availability.Should().Be(HushVotingLicenceAvailability.AutomaticUpgrade);
    }

    [Fact]
    public void Enterprise_HasExactV1Semantics()
    {
        var plan = _catalogue.FindPlan(HushVotingLicencePlanId.Enterprise)!;

        plan.Family.Should().Be(HushVotingLicenceFamily.Enterprise);
        plan.DisplayName.Should().Be("HushVoting! Enterprise");
        plan.DisplayOrder.Should().Be(50);
        plan.UpgradeRank.Should().Be(4000);
        plan.EligibleVoterCap.Should().BeNull();
        plan.UnlimitedElections.Should().BeTrue();
        plan.Availability.Should().Be(HushVotingLicenceAvailability.Unavailable);
        plan.UnavailableSafeReason.Should().Contain("not yet available");
        plan.SafeDescription.Should().Contain("Contact provider - not yet available");
        plan.GovernanceOptions.Should().BeEmpty();
    }

    [Fact]
    public void GovernanceOptions_AreExactAndCumulativePerTier()
    {
        Direct(_catalogue).Select(static o => o.Id.Value).Should().Equal("no-customer-trustees");
        Veritas500(_catalogue).Select(static o => o.Id.Value).Should().Equal(
            "no-customer-trustees", "trustees-3of5");
        Veritas2000(_catalogue).Select(static o => o.Id.Value).Should().Equal(
            "no-customer-trustees", "trustees-3of5", "trustees-7of10");
        Veritas10000(_catalogue).Select(static o => o.Id.Value).Should().Equal(
            "no-customer-trustees", "trustees-3of5", "trustees-7of10", "trustees-8of13");

        static IReadOnlyList<HushVotingGovernanceOption> Direct(HushVotingLicenceCatalogue c) =>
            c.FindPlan(HushVotingLicencePlanId.DirectFree)!.GovernanceOptions;
        static IReadOnlyList<HushVotingGovernanceOption> Veritas500(HushVotingLicenceCatalogue c) =>
            c.FindPlan(HushVotingLicencePlanId.Veritas500)!.GovernanceOptions;
        static IReadOnlyList<HushVotingGovernanceOption> Veritas2000(HushVotingLicenceCatalogue c) =>
            c.FindPlan(HushVotingLicencePlanId.Veritas2000)!.GovernanceOptions;
        static IReadOnlyList<HushVotingGovernanceOption> Veritas10000(HushVotingLicenceCatalogue c) =>
            c.FindPlan(HushVotingLicencePlanId.Veritas10000)!.GovernanceOptions;
    }

    [Fact]
    public void GovernanceOptions_SupportBothBindingAndNonBinding()
    {
        var all = _catalogue.Plans.SelectMany(static p => p.GovernanceOptions).Distinct().ToArray();

        foreach (var option in all)
        {
            option.SupportsBindingStatus(HushVotingBindingStatus.NonBinding).Should().BeTrue();
            option.SupportsBindingStatus(HushVotingBindingStatus.Binding).Should().BeTrue();
        }
    }

    [Fact]
    public void ZeroCustomerTrustees_ProjectsZeroZero_WhileMappingToAdmin1Of1()
    {
        var zero = _catalogue.FindPlan(HushVotingLicencePlanId.Veritas500)!
            .GetGovernanceOption(HushVotingGovernanceOptionId.NoCustomerTrustees)!;

        zero.CustomerTrusteeCount.Should().Be(0);
        zero.RequiredApprovalCount.Should().Be(0);

        var mapping = HushVotingProfileCompatibilityV1.Resolve(
            HushVotingGovernanceOptionId.NoCustomerTrustees,
            HushVotingBindingStatus.Binding)!;
        mapping.RuntimeProfileId.Should().Be("admin-prod-1of1");

        var devMapping = HushVotingProfileCompatibilityV1.Resolve(
            HushVotingGovernanceOptionId.NoCustomerTrustees,
            HushVotingBindingStatus.NonBinding)!;
        devMapping.RuntimeProfileId.Should().Be("admin-dev-1of1");
    }

    [Fact]
    public void FixedTrusteeMappings_AreExact()
    {
        var expectations = new[]
        {
            ("trustees-3of5", HushVotingBindingStatus.NonBinding, "dkg-dev-3of5"),
            ("trustees-3of5", HushVotingBindingStatus.Binding, "dkg-prod-3of5"),
            ("trustees-7of10", HushVotingBindingStatus.NonBinding, "dkg-dev-7of10"),
            ("trustees-7of10", HushVotingBindingStatus.Binding, "dkg-prod-7of10"),
            ("trustees-8of13", HushVotingBindingStatus.NonBinding, "dkg-dev-8of13"),
            ("trustees-8of13", HushVotingBindingStatus.Binding, "dkg-prod-8of13"),
        };

        foreach (var (optionValue, binding, profile) in expectations)
        {
            var entry = HushVotingProfileCompatibilityV1.Resolve(
                HushVotingGovernanceOptionId.FromExternal(optionValue),
                binding)!;

            entry.RuntimeProfileId.Should().Be(profile);
            entry.DevOnly.Should().Be(binding == HushVotingBindingStatus.NonBinding);
        }
    }

    [Fact]
    public void IndexedLookup_ReturnsPlansAndIsDeterministic()
    {
        _catalogue.ContainsPlan(HushVotingLicencePlanId.Veritas2000).Should().BeTrue();
        _catalogue.FindPlan(HushVotingLicencePlanId.Enterprise)!.DisplayOrder.Should().Be(50);
        _catalogue.FindPlan(HushVotingLicencePlanId.FromExternal("unknown.plan")).Should().BeNull();
    }
}

public sealed class HushVotingLicencePlanConstructionTests
{
    [Fact]
    public void Plan_RejectsUnknownPlanId()
    {
        var act = () => new HushVotingLicencePlan(
            HushVotingLicencePlanId.FromExternal("not-a-plan"),
            HushVotingLicenceFamily.Direct,
            "x",
            "y",
            1,
            1,
            1,
            true,
            HushVotingLicenceTerm.Perpetual,
            HushVotingLicenceAvailability.Default,
            null,
            Array.Empty<HushVotingGovernanceOption>(),
            HushVotingLicenceCatalogueVersion.V1);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Plan_RejectsEmptyDisplayNameAndNegativeValues()
    {
        var act = () => new HushVotingLicencePlan(
            HushVotingLicencePlanId.DirectFree,
            HushVotingLicenceFamily.Direct,
            "   ",
            "safe",
            1,
            1,
            1,
            true,
            HushVotingLicenceTerm.Perpetual,
            HushVotingLicenceAvailability.Default,
            null,
            Array.Empty<HushVotingGovernanceOption>(),
            HushVotingLicenceCatalogueVersion.V1);

        act.Should().Throw<ArgumentException>();

        var negative = () => new HushVotingLicencePlan(
            HushVotingLicencePlanId.DirectFree,
            HushVotingLicenceFamily.Direct,
            "name",
            "safe",
            -1,
            1,
            1,
            true,
            HushVotingLicenceTerm.Perpetual,
            HushVotingLicenceAvailability.Default,
            null,
            Array.Empty<HushVotingGovernanceOption>(),
            HushVotingLicenceCatalogueVersion.V1);

        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Catalogue_RejectsDuplicatePlansAndWrongVersion()
    {
        var plan = HushVotingLicenceCatalogueV1.CreatePlans()[0];

        var duplicate = () => new HushVotingLicenceCatalogue(
            HushVotingLicenceCatalogueVersion.V1,
            [plan, plan],
            Array.Empty<HushVotingProfileCompatibilityEntry>());

        duplicate.Should().Throw<ArgumentException>().WithMessage("*Duplicate plan id*");

        var wrongVersion = () => new HushVotingLicencePlan(
            HushVotingLicencePlanId.DirectFree,
            HushVotingLicenceFamily.Direct,
            "HushVoting! Direct Free",
            "safe",
            10,
            0,
            100,
            true,
            HushVotingLicenceTerm.Perpetual,
            HushVotingLicenceAvailability.Default,
            null,
            Array.Empty<HushVotingGovernanceOption>(),
            HushVotingLicenceCatalogueVersion.FromExternal("hushvoting-licence-catalogue/v9.9.9"));

        wrongVersion.Should().Throw<ArgumentException>();
    }
}

public sealed class HushVotingLicenceSafeProjectionContractTests
{
    [Fact]
    public void PlanProjection_ContainsOnlyApprovedPublicFields()
    {
        var plan = HushVotingLicenceCatalogueV1.CreateCatalogue()
            .FindPlan(HushVotingLicencePlanId.Veritas2000)!;

        var projection = new HushVotingLicencePlanProjection(
            plan.Id,
            plan.Family,
            plan.DisplayName,
            plan.SafeDescription,
            plan.DisplayOrder,
            plan.EligibleVoterCap,
            plan.UnlimitedElections,
            plan.Term,
            plan.Availability,
            plan.UnavailableSafeReason,
            plan.GovernanceOptions.Select(HushVotingGovernanceOptionProjection.From).ToArray(),
            plan.CatalogueVersion);

        projection.PlanId.Value.Should().Be(HushVotingLicencePlanId.Veritas2000Value);
        projection.Family.Should().Be(HushVotingLicenceFamily.Veritas);
        projection.EligibleVoterCap.Should().Be(2000);
        projection.Term.IsOneCalendarYear.Should().BeTrue();
        projection.GovernanceOptions.Select(static o => o.Id.Value).Should().Equal(
            "no-customer-trustees", "trustees-3of5", "trustees-7of10");

        // Safe projection exposes no internal profile/trustee metadata on the plan itself.
        projection.GetType().GetProperties().Select(static p => p.Name).Should().NotContain(
            new[] { "RuntimeProfileId", "ProviderKey", "ProfileVersion", "Internal" });
    }

    [Fact]
    public void GovernanceOptionProjection_CarriesCustomerFacingMetadata()
    {
        var source = HushVotingLicenceCatalogueV1.CreateCatalogue()
            .FindPlan(HushVotingLicencePlanId.Veritas10000)!
            .GetGovernanceOption(HushVotingGovernanceOptionId.Trustees8Of13)!;

        var projection = HushVotingGovernanceOptionProjection.From(source);

        projection.CustomerTrusteeCount.Should().Be(13);
        projection.RequiredApprovalCount.Should().Be(8);
        projection.SafeLabel.Should().Be("8 of 13 trustees");
        projection.SupportedBindingStatuses.Should().HaveCount(2);
    }
}
