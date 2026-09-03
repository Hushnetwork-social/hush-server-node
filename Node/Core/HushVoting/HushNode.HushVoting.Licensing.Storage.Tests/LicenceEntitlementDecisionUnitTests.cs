using FluentAssertions;
using HushNode.HushVoting.Licensing.Storage;
using HushShared.HushVoting.Licensing.Model;
using Xunit;

namespace HushNode.HushVoting.Licensing.Storage.Tests;

/// <summary>
/// FEAT-013 Task 3.4 unit coverage for the pure entitlement decisions behind GetOrProvision and
/// expiry normalization: canonical subject construction, migration provenance, calendar-year expiry
/// (leap-day safe), operative-snapshot pinning, canonical fingerprinting, and stable labels.
/// </summary>
public sealed class LicenceEntitlementDecisionUnitTests
{
    private static HushVotingLicenceCatalogue Catalogue => HushVotingLicenceCatalogueV1.CreateCatalogue();

    // ------------------------------------------------------------------ subject normalization

    [Fact]
    public void NormalizeCanonicalAddress_trims_and_lowercases()
    {
        AuthenticatedIdentitySubject.NormalizeCanonicalAddress("  AbC123  ")
            .Should().Be("abc123");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeCanonicalAddress_rejects_empty_or_whitespace(string? address)
    {
        AuthenticatedIdentitySubject.NormalizeCanonicalAddress(address).Should().BeNull();
    }

    [Fact]
    public void NormalizeCanonicalAddress_rejects_oversized_address()
    {
        AuthenticatedIdentitySubject.NormalizeCanonicalAddress(new string('a', 161))
            .Should().BeNull();
        AuthenticatedIdentitySubject.NormalizeCanonicalAddress(new string('a', 160))
            .Should().NotBeNull();
    }

    [Fact]
    public void TryCreate_builds_trusted_subject_from_canonical_values()
    {
        var created = AuthenticatedIdentitySubject.TryCreate(
            LicencePersistenceVocabulary.SubjectTypeIdentity,
            "AbC123",
            42,
            out var subject,
            out var error);

        created.Should().BeTrue();
        error.Should().BeNull();
        subject.Should().NotBeNull();
        subject!.SubjectType.Should().Be(LicencePersistenceVocabulary.SubjectTypeIdentity);
        subject.CanonicalPublicSigningAddress.Should().Be("abc123");
        subject.IdentityCreationBlockIndex.Should().Be(42);
    }

    [Fact]
    public void TryCreate_rejects_unknown_subject_type_with_stable_code()
    {
        AuthenticatedIdentitySubject.TryCreate("Device", "abc", 1, out _, out var error)
            .Should().BeFalse();
        error.Should().Be(AuthenticatedIdentitySubject.ErrorInvalidSubjectType);
    }

    [Fact]
    public void TryCreate_rejects_invalid_address_with_stable_code()
    {
        AuthenticatedIdentitySubject.TryCreate(
            LicencePersistenceVocabulary.SubjectTypeIdentity,
            "   ",
            1,
            out _,
            out var error).Should().BeFalse();
        error.Should().Be(AuthenticatedIdentitySubject.ErrorInvalidAddress);
    }

    [Fact]
    public void TryCreate_rejects_negative_creation_block_with_stable_code()
    {
        AuthenticatedIdentitySubject.TryCreate(
            LicencePersistenceVocabulary.SubjectTypeIdentity,
            "abc",
            -1,
            out _,
            out var error).Should().BeFalse();
        error.Should().Be(AuthenticatedIdentitySubject.ErrorNegativeCreationBlock);
    }

    // ------------------------------------------------------------------ migration provenance

    [Fact]
    public void DecideProvisionSource_identity_at_or_before_watermark_is_migration()
    {
        LicenceEntitlementDecisions.DecideProvisionSource(1000, 1000)
            .Should().Be(LicencePersistenceVocabulary.SourceMigrationLazyDefault);
        LicenceEntitlementDecisions.DecideProvisionSource(999, 1000)
            .Should().Be(LicencePersistenceVocabulary.SourceMigrationLazyDefault);
    }

    [Fact]
    public void DecideProvisionSource_identity_after_watermark_is_default()
    {
        LicenceEntitlementDecisions.DecideProvisionSource(1001, 1000)
            .Should().Be(LicencePersistenceVocabulary.SourceDefaultFree);
    }

    // ------------------------------------------------------------------ calendar-year expiry

    [Fact]
    public void ComputeExpiryInstant_uses_calendar_add_years_never_365_days()
    {
        LicenceEntitlementDecisions.ComputeExpiryInstant(
                new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
                HushVotingLicenceTerm.OneCalendarYear)
            .Should().Be(new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ComputeExpiryInstant_leap_day_rolls_to_feb_28_upper_exclusive()
    {
        LicenceEntitlementDecisions.ComputeExpiryInstant(
                new DateTime(2024, 2, 29, 0, 0, 0, DateTimeKind.Utc),
                HushVotingLicenceTerm.OneCalendarYear)
            .Should().Be(new DateTime(2025, 2, 28, 0, 0, 0, DateTimeKind.Utc));

        // 2028 is a leap year: 2027-02-28 + 1 calendar year stays on Feb 28 (upper-exclusive).
        LicenceEntitlementDecisions.ComputeExpiryInstant(
                new DateTime(2027, 2, 28, 0, 0, 0, DateTimeKind.Utc),
                HushVotingLicenceTerm.OneCalendarYear)
            .Should().Be(new DateTime(2028, 2, 28, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ComputeExpiryInstant_perpetual_has_no_expiry()
    {
        LicenceEntitlementDecisions.ComputeExpiryInstant(
                new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                HushVotingLicenceTerm.Perpetual)
            .Should().BeNull();
    }

    [Theory]
    [InlineData("2026-01-15T11:59:59Z", false)]  // before the upper-exclusive bound -> not expired
    [InlineData("2026-01-15T12:00:00Z", true)]   // at the bound -> expired
    [InlineData("2026-01-15T12:00:01Z", true)]   // after the bound -> expired
    public void IsExpired_uses_upper_exclusive_boundary(string now, bool expected)
    {
        var annual = new LicenceAssignmentEntity
        {
            LicenceAssignmentId = Guid.NewGuid(),
            PlanId = HushVotingLicencePlanId.Veritas500.Value,
            LifecycleStatus = LicencePersistenceVocabulary.LifecycleActive,
            Source = LicencePersistenceVocabulary.SourceAutomaticUpgrade,
            EffectiveFromUtc = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            ExpiresAtUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            PlanFamily = LicencePersistenceVocabulary.PlanFamilyVeritas,
            TermKind = LicencePersistenceVocabulary.TermAnnual,
            TermYears = 1,
        };

        LicenceEntitlementDecisions.IsExpired(annual, DateTime.Parse(now).ToUniversalTime())
            .Should().Be(expected);
    }

    [Fact]
    public void IsExpired_perpetual_assignment_never_expires()
    {
        var perpetual = new LicenceAssignmentEntity
        {
            LicenceAssignmentId = Guid.NewGuid(),
            PlanId = HushVotingLicencePlanId.DirectFree.Value,
            LifecycleStatus = LicencePersistenceVocabulary.LifecycleActive,
            Source = LicencePersistenceVocabulary.SourceDefaultFree,
            EffectiveFromUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ExpiresAtUtc = null,
            PlanFamily = LicencePersistenceVocabulary.PlanFamilyDirect,
            TermKind = LicencePersistenceVocabulary.TermPerpetual,
            TermYears = 0,
        };

        LicenceEntitlementDecisions.IsExpired(perpetual, DateTime.UtcNow).Should().BeFalse();
    }

    // ------------------------------------------------------------------ operative snapshot pinning

    [Fact]
    public void ToOperativeSnapshot_direct_free_is_perpetual_cap_100_rank_0()
    {
        var plan = Catalogue.FindPlan(HushVotingLicencePlanId.DirectFree)!;
        var snapshot = LicenceEntitlementDecisions.ToOperativeSnapshot(plan);

        snapshot.PlanFamily.Should().Be(LicencePersistenceVocabulary.PlanFamilyDirect);
        snapshot.UpgradeRank.Should().Be(0);
        snapshot.EligibleVoterCap.Should().Be(100);
        snapshot.UnlimitedElectionPolicy.Should().BeTrue();
        snapshot.TermKind.Should().Be(LicencePersistenceVocabulary.TermPerpetual);
        snapshot.TermYears.Should().Be(0);
        snapshot.AllowedGovernanceOptionIds.Should().BeEquivalentTo(
            [HushVotingGovernanceOptionId.NoCustomerTrustees.Value]);
    }

    [Fact]
    public void ToOperativeSnapshot_veritas_500_is_annual_cap_500_rank_1000()
    {
        var plan = Catalogue.FindPlan(HushVotingLicencePlanId.Veritas500)!;
        var snapshot = LicenceEntitlementDecisions.ToOperativeSnapshot(plan);

        snapshot.PlanFamily.Should().Be(LicencePersistenceVocabulary.PlanFamilyVeritas);
        snapshot.UpgradeRank.Should().Be(1000);
        snapshot.EligibleVoterCap.Should().Be(500);
        snapshot.TermKind.Should().Be(LicencePersistenceVocabulary.TermAnnual);
        snapshot.TermYears.Should().Be(1);
        snapshot.AllowedGovernanceOptionIds.Should().BeEquivalentTo(
        [
            HushVotingGovernanceOptionId.NoCustomerTrustees.Value,
            HushVotingGovernanceOptionId.Trustees3Of5.Value,
        ]);
    }

    // ------------------------------------------------------------------ upgrade-evaluation mapping

    [Fact]
    public void MapUpgradeEvaluationToDurableResult_covers_the_closed_policy_codes()
    {
        var catalogue = Catalogue;
        var direct = HushVotingLicencePlanId.DirectFree;
        var v500 = HushVotingLicencePlanId.Veritas500;
        var v2000 = HushVotingLicencePlanId.Veritas2000;
        var v10000 = HushVotingLicencePlanId.Veritas10000;
        var enterprise = HushVotingLicencePlanId.Enterprise;

        Map(HushVotingLicenceUpgradeEvaluator.Evaluate(catalogue, direct, v500))
            .Should().Be(LicenceActivationOutcome.Activated);
        Map(HushVotingLicenceUpgradeEvaluator.Evaluate(catalogue, v500, v2000))
            .Should().Be(LicenceActivationOutcome.Activated);
        Map(HushVotingLicenceUpgradeEvaluator.Evaluate(catalogue, v2000, v10000))
            .Should().Be(LicenceActivationOutcome.Activated);

        // same plan -> unchanged
        Map(HushVotingLicenceUpgradeEvaluator.Evaluate(catalogue, v500, v500))
            .Should().Be(LicenceActivationOutcome.TransitionUnchanged);
        // lower rank -> not higher
        Map(HushVotingLicenceUpgradeEvaluator.Evaluate(catalogue, v2000, v500))
            .Should().Be(LicenceActivationOutcome.TransitionNotHigher);
        // downgrade to Direct Free -> not higher
        Map(HushVotingLicenceUpgradeEvaluator.Evaluate(catalogue, v500, direct))
            .Should().Be(LicenceActivationOutcome.TransitionNotHigher);
        // Enterprise is never self-activated -> unavailable
        Map(HushVotingLicenceUpgradeEvaluator.Evaluate(catalogue, v500, enterprise))
            .Should().Be(LicenceActivationOutcome.PlanUnavailable);
        // unknown target -> plan unknown
        Map(HushVotingLicenceUpgradeEvaluator.Evaluate(catalogue, v500, HushVotingLicencePlanId.FromExternal("hushvoting.veritas.999")))
            .Should().Be(LicenceActivationOutcome.PlanUnknown);
    }

    private static LicenceActivationOutcome Map(HushVotingLicenceUpgradeEvaluation evaluation) =>
        LicenceEntitlementDecisions.MapUpgradeEvaluationToDurableResult(evaluation);

    // ------------------------------------------------------------------ canonical fingerprint

    [Fact]
    public void CanonicalActivationFingerprint_is_deterministic_and_sha256_hex()
    {
        var first = LicenceEntitlementDecisions.CanonicalActivationFingerprint(
            HushVotingLicencePlanId.DirectFree.Value, 1, HushVotingLicencePlanId.Veritas500.Value);
        var second = LicenceEntitlementDecisions.CanonicalActivationFingerprint(
            HushVotingLicencePlanId.DirectFree.Value, 1, HushVotingLicencePlanId.Veritas500.Value);

        first.Should().Be(second);
        first.Should().MatchRegex("^[0-9A-F]{64}$");
    }

    [Fact]
    public void CanonicalActivationFingerprint_changes_when_any_command_field_changes()
    {
        var target = HushVotingLicencePlanId.Veritas500.Value;
        var baseline = LicenceEntitlementDecisions.CanonicalActivationFingerprint(
            HushVotingLicencePlanId.DirectFree.Value, 1, target);

        LicenceEntitlementDecisions.CanonicalActivationFingerprint(
                HushVotingLicencePlanId.DirectFree.Value, 2, target)
            .Should().NotBe(baseline);
        LicenceEntitlementDecisions.CanonicalActivationFingerprint(
                HushVotingLicencePlanId.Veritas500.Value, 1, target)
            .Should().NotBe(baseline);
        LicenceEntitlementDecisions.CanonicalActivationFingerprint(
                HushVotingLicencePlanId.DirectFree.Value, 1, HushVotingLicencePlanId.Veritas2000.Value)
            .Should().NotBe(baseline);
    }

    [Fact]
    public void CanonicalActivationFingerprint_is_unambiguous_across_field_boundaries()
    {
        // Length-prefixed plan segments mean field-boundary permutations cannot collide.
        LicenceEntitlementDecisions.CanonicalActivationFingerprint("a", 1, "b")
            .Should().NotBe(LicenceEntitlementDecisions.CanonicalActivationFingerprint("a|1", 0, "b"));
        LicenceEntitlementDecisions.CanonicalActivationFingerprint("ab", 1, "c")
            .Should().NotBe(LicenceEntitlementDecisions.CanonicalActivationFingerprint("a", 1, "bc"));
    }

    // ------------------------------------------------------------------ stable labels

    [Theory]
    [InlineData(LicenceResolutionOutcome.ResolvedExisting, "resolved_existing")]
    [InlineData(LicenceResolutionOutcome.ProvisionedDefault, "provisioned_default")]
    [InlineData(LicenceResolutionOutcome.ProvisionedMigrationDefault, "provisioned_migration_default")]
    [InlineData(LicenceResolutionOutcome.ExpiredToDefault, "expired_to_default")]
    [InlineData(LicenceResolutionOutcome.StorageUnavailable, "storage_unavailable")]
    public void ResolutionOutcome_wire_names_are_stable(LicenceResolutionOutcome outcome, string expected)
    {
        LicenceEntitlementOutcomeNames.ToWireName(outcome).Should().Be(expected);
    }

    [Fact]
    public void ActivationOutcome_wire_names_and_durable_set_are_closed()
    {
        LicenceEntitlementOutcomeNames.ToWireName(LicenceActivationOutcome.Activated).Should().Be("activated");
        LicenceEntitlementOutcomeNames.ToWireName(LicenceActivationOutcome.ConcurrencyExhausted).Should().Be("concurrency_exhausted");
        LicenceEntitlementOutcomeNames.IsDurableOperationResult(LicenceActivationOutcome.Activated).Should().BeTrue();
        LicenceEntitlementOutcomeNames.IsDurableOperationResult(LicenceActivationOutcome.PreconditionConflict).Should().BeTrue();
        LicenceEntitlementOutcomeNames.IsDurableOperationResult(LicenceActivationOutcome.IdempotencyPayloadMismatch).Should().BeFalse();
        LicenceEntitlementOutcomeNames.IsDurableOperationResult(LicenceActivationOutcome.StorageUnavailable).Should().BeFalse();

        LicenceEntitlementOutcomeNames.FromDurableResultString(LicencePersistenceVocabulary.OperationResultActivated)
            .Should().Be(LicenceActivationOutcome.Activated);
        LicenceEntitlementOutcomeNames.FromDurableResultString(LicencePersistenceVocabulary.OperationResultEntitlementNotInitialized)
            .Should().Be(LicenceActivationOutcome.EntitlementNotInitialized);
    }

    [Fact]
    public void Failure_codes_are_stable_and_distinct()
    {
        LicenceEntitlementFailureCodes.StorageUnavailable.Should().Be("storage_unavailable");
        LicenceEntitlementFailureCodes.ConcurrencyExhausted.Should().Be("concurrency_exhausted");
        LicenceEntitlementFailureCodes.PersistenceInvariantViolation.Should().Be("persistence_invariant_violation");
        LicenceEntitlementFailureCodes.CatalogueIncompatible.Should().Be("catalogue_incompatible");
    }

    // ------------------------------------------------------------------ configuration contract

    [Fact]
    public void LicenceServiceConfiguration_requires_known_version_digest_and_direct_free()
    {
        var catalogue = Catalogue;
        LicenceServiceConfiguration.CreateDefault().Should().NotBeNull();

        LicenceServiceConfiguration.TryCreate(
                catalogue.Version.Value,
                "not-a-digest",
                HushVotingLicenceCatalogueVersion.V1SchemaId,
                catalogue,
                out _,
                out var error)
            .Should().BeFalse();
        error.Should().Be(LicenceServiceConfiguration.ErrorInvalidReleaseDigest);
    }

    [Fact]
    public void LicenceServiceConfiguration_requires_current_catalogue_contains_direct_free()
    {
        var directFree = Catalogue.FindPlan(HushVotingLicencePlanId.DirectFree)!;
        var withoutDirectFree = new HushVotingLicenceCatalogue(
            HushVotingLicenceCatalogueVersion.V1,
            Catalogue.Plans.Where(p => p.Id != HushVotingLicencePlanId.DirectFree).ToArray(),
            HushVotingProfileCompatibilityV1.Entries);

        LicenceServiceConfiguration.TryCreate(
                HushVotingLicenceCatalogueVersion.V1.Value,
                new string('A', 64),
                HushVotingLicenceCatalogueVersion.V1SchemaId,
                withoutDirectFree,
                out _,
                out var error)
            .Should().BeFalse();
        error.Should().Be(LicenceServiceConfiguration.ErrorMissingDirectFreePlan);
    }
}
