// FEAT-015 Task 2.3 — contract freeze tests for the sole licence payload + vocabulary.
//
// These tests pin the closed payload contract BEFORE any consumer/codec work:
//  - the sole kind GUID 71370664-5eb4-4ce9-b96a-d7e7ffe53db5 (immutable);
//  - exactly two closed intents with exact wire strings;
//  - the payload exposes ONLY the five bounded client-authorable members (reflection
//    guard: no licence id, identity, address, date, cap, governance, rank, lifecycle,
//    source, payment, or server-decision member may ever appear);
//  - canonical member names/order are frozen;
//  - the outer TransactionId is the public licence reference (no second LicenceId);
//  - the 20-code stable registry matches the FeatureDescription list verbatim and
//    unknown codes fail closed;
//  - structural shape guard: baseline carries no expected-current precondition,
//    upgrade requires both, unknown intent/bounds/malformed UUIDs fail closed.

using System.Reflection;
using FluentAssertions;
using Xunit;

namespace HushNode.HushVoting.Licence.Transactions.Tests;

public sealed class HushVotingLicenceAssignmentPayloadContractTests
{
    [Fact]
    public void Payload_kind_guid_is_the_frozen_authoritative_value()
    {
        HushVotingLicenceAssignmentPayloadHandler.LicenceAssignmentPayloadKind.ToString()
            .Should().Be("71370664-5eb4-4ce9-b96a-d7e7ffe53db5");

        // Kind is the exact FEAT-015 domain separator; never the FullIdentity/UpdateIdentity kinds.
        HushVotingLicenceAssignmentPayloadHandler.LicenceAssignmentPayloadKind
            .Should().NotBe(Guid.Parse("351cd60b-3fdf-48d4-b608-e93c0100f7d0"));
        HushVotingLicenceAssignmentPayloadHandler.LicenceAssignmentPayloadKind
            .Should().NotBe(Guid.Parse("a7e3c4b2-1f8d-4e5a-9c6b-2d3e4f5a6b7c"));
    }

    [Fact]
    public void Payload_kind_matcher_is_exact_and_fail_closed()
    {
        HushVotingLicenceAssignmentPayloadHandler.IsLicencePayloadKind(
                HushVotingLicenceAssignmentPayloadHandler.LicenceAssignmentPayloadKind)
            .Should().BeTrue();
        HushVotingLicenceAssignmentPayloadHandler.IsLicencePayloadKind(Guid.NewGuid())
            .Should().BeFalse();
        HushVotingLicenceAssignmentPayloadHandler.IsLicencePayloadKind(Guid.Empty)
            .Should().BeFalse();
    }

    [Fact]
    public void Payload_exposes_only_the_five_frozen_client_authorable_members()
    {
        var allowed = new[]
        {
            "TransitionIntent",
            "RequestedPlanId",
            "ObservedCatalogueVersion",
            "ExpectedCurrentLicenceTransactionId",
            "ExpectedCurrentPlanId",
        };

        var actual = typeof(HushVotingLicenceAssignmentPayload)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        actual.Should().BeEquivalentTo(
            allowed.OrderBy(name => name, StringComparer.Ordinal));

        // No second licence identifier, identity, address, server-decision, date, cap,
        // governance, rank, lifecycle, source, payment, or Enterprise member is allowed.
        actual.Should().NotContain(new[]
        {
            "LicenceId", "TransactionId", "IdentityAddress", "SubjectId", "EffectiveFromUtc",
            "ExpiresAtUtc", "EligibleVoterCap", "GovernanceOptionIds", "UpgradeRank",
            "Lifecycle", "Source", "Payment", "Price", "EnterpriseRequest",
        });
    }

    [Fact]
    public void Canonical_member_names_and_order_are_frozen()
    {
        HushVotingLicencePayloadCanonicalMembers.Order.Should().Equal(new[]
        {
            "TransitionIntent",
            "RequestedPlanId",
            "ObservedCatalogueVersion",
            "ExpectedCurrentLicenceTransactionId",
            "ExpectedCurrentPlanId",
        });

        HushVotingLicencePayloadCanonicalMembers.BaselineMembers.Should().Equal(new[]
        {
            "TransitionIntent",
            "RequestedPlanId",
            "ObservedCatalogueVersion",
        });

        HushVotingLicencePayloadCanonicalMembers.UpgradeMembers.Should()
            .Equal(HushVotingLicencePayloadCanonicalMembers.Order);
    }

    [Fact]
    public void Transaction_uuid_is_the_public_licence_reference_there_is_no_second_licence_id()
    {
        // The licence reference is the OUTER transaction TransactionId; the payload must
        // never carry its own licence id. Proven structurally by the reflection guard and
        // by the canonical member freeze (no LicenceId/TransactionId member).
        var payload = new HushVotingLicenceAssignmentPayload(
            HushVotingLicenceTransitionIntent.BaselineFree,
            HushShared.HushVoting.Licensing.Model.HushVotingLicencePlanId.DirectFreeValue,
            HushShared.HushVoting.Licensing.Model.HushVotingLicenceCatalogueVersion.V1Value);

        payload.Should().NotBeNull();
    }
}

public sealed class HushVotingLicenceTransitionIntentContractTests
{
    [Fact]
    public void Intent_vocabulary_is_exactly_the_two_closed_v1_values()
    {
        HushVotingLicenceTransitionIntent.Known.Should().Equal(new[]
        {
            "baseline_free",
            "confirmed_upgrade",
        });
    }

    [Theory]
    [InlineData("baseline_free")]
    [InlineData("confirmed_upgrade")]
    public void Known_intents_parse_to_the_exact_canonical_value(string value)
    {
        HushVotingLicenceTransitionIntent.TryParse(value, out var parsed).Should().BeTrue();
        parsed.Should().Be(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("BASELINE_FREE")]
    [InlineData("automatic_upgrade")]
    [InlineData("downgrade")]
    [InlineData("enterprise")]
    [InlineData("baseline free")]
    public void Unknown_or_malformed_intents_fail_closed(string? value)
    {
        HushVotingLicenceTransitionIntent.TryParse(value, out var parsed).Should().BeFalse();
        parsed.Should().BeEmpty();
    }
}

public sealed class HushVotingLicenceValidationCodesContractTests
{
    /// <summary>The FeatureDescription normative list (verbatim, 20 entries).</summary>
    private static readonly string[] NormativeCodes =
    {
        "LICENCE_PAYLOAD_KIND_UNSUPPORTED",
        "LICENCE_PAYLOAD_MALFORMED",
        "LICENCE_PAYLOAD_SIZE_MISMATCH",
        "LICENCE_SIGNATURE_INVALID",
        "LICENCE_SIGNATORY_IDENTITY_NOT_FOUND",
        "LICENCE_INTENT_UNKNOWN",
        "LICENCE_PLAN_UNKNOWN",
        "LICENCE_PLAN_UNAVAILABLE",
        "LICENCE_ENTERPRISE_ADMIN_ONLY",
        "LICENCE_CATALOGUE_STALE",
        "LICENCE_BASELINE_REQUIRES_NO_ACTIVE_ENTITLEMENT",
        "LICENCE_UPGRADE_REQUIRES_ACTIVE_ENTITLEMENT",
        "LICENCE_EXPECTED_CURRENT_INVALID",
        "LICENCE_PRECONDITION_STALE",
        "LICENCE_TRANSITION_UNCHANGED",
        "LICENCE_TRANSITION_NOT_HIGHER",
        "LICENCE_TRANSITION_PENDING",
        "LICENCE_TRANSACTION_IDEMPOTENCY_MISMATCH",
        "LICENCE_INDEX_AUTHORITY_UNAVAILABLE",
        "LICENCE_PERSISTENCE_INVARIANT_VIOLATION",
    };

    [Fact]
    public void Code_registry_is_exactly_the_normative_closed_set()
    {
        HushVotingLicenceValidationCodes.Known.Should().Equal(NormativeCodes);
    }

    [Fact]
    public void Registry_has_no_duplicate_or_empty_code()
    {
        HushVotingLicenceValidationCodes.Known.Distinct(StringComparer.Ordinal)
            .Count().Should().Be(NormativeCodes.Length);
        HushVotingLicenceValidationCodes.Known.Should().NotContain(string.Empty);
    }

    [Fact]
    public void Known_codes_are_recognised()
    {
        foreach (var code in NormativeCodes)
        {
            HushVotingLicenceValidationCodes.IsKnown(code).Should().BeTrue($"code {code} must be known");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ACCEPTED")]
    [InlineData("LICENCE_TRANSITION_WHATEVER")]
    [InlineData("licence_plan_unknown")]
    [InlineData("LICENCE_VALID")]
    public void Unknown_codes_fail_closed(string? code)
    {
        HushVotingLicenceValidationCodes.IsKnown(code).Should().BeFalse();
    }
}

public sealed class HushVotingLicencePayloadShapeGuardTests
{
    private const string DirectFree = "hushvoting.direct.free";
    private const string Veritas500 = "hushvoting.veritas.500";
    private const string Veritas2000 = "hushvoting.veritas.2000";
    private const string CatalogueV1 = "hushvoting-licence-catalogue/v1.0.0";

    private static readonly Guid CanonicalCurrentTransaction = Guid.Parse("6f7b3a51-2c8e-4d2a-9b14-1e5c7d9f0a2b");

    [Fact]
    public void Valid_baseline_payload_is_accepted()
    {
        var payload = new HushVotingLicenceAssignmentPayload(
            HushVotingLicenceTransitionIntent.BaselineFree, DirectFree, CatalogueV1);

        var result = HushVotingLicencePayloadShapeGuard.Validate(payload);

        result.IsValid.Should().BeTrue();
        result.ValidationCode.Should().BeNull();
        result.Payload.Should().BeSameAs(payload);
    }

    [Fact]
    public void Valid_upgrade_payload_is_accepted()
    {
        var payload = new HushVotingLicenceAssignmentPayload(
            HushVotingLicenceTransitionIntent.ConfirmedUpgrade,
            Veritas2000,
            CatalogueV1,
            CanonicalCurrentTransaction,
            DirectFree);

        var result = HushVotingLicencePayloadShapeGuard.Validate(payload);

        result.IsValid.Should().BeTrue();
        result.ValidationCode.Should().BeNull();
    }

    [Fact]
    public void Null_payload_fails_closed_as_malformed()
    {
        var result = HushVotingLicencePayloadShapeGuard.Validate(null);

        result.IsValid.Should().BeFalse();
        result.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.PayloadMalformed);
    }

    [Fact]
    public void Baseline_must_not_carry_expected_current_preconditions()
    {
        var baselineWithExpectedCurrent = new HushVotingLicenceAssignmentPayload(
            HushVotingLicenceTransitionIntent.BaselineFree,
            DirectFree,
            CatalogueV1,
            CanonicalCurrentTransaction,
            DirectFree);

        var result = HushVotingLicencePayloadShapeGuard.Validate(baselineWithExpectedCurrent);

        result.IsValid.Should().BeFalse();
        result.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.ExpectedCurrentInvalid);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("6f7b3a51-2c8e-4d2a-9b14-1e5c7d9f0a2b", null)]
    [InlineData(null, "hushvoting.direct.free")]
    public void Upgrade_requires_both_expected_current_members(
        string? expectedCurrentTransactionId,
        string? expectedCurrentPlanId)
    {
        var upgradeMissingPrecondition = new HushVotingLicenceAssignmentPayload(
            HushVotingLicenceTransitionIntent.ConfirmedUpgrade,
            Veritas2000,
            CatalogueV1,
            expectedCurrentTransactionId is null ? null : Guid.Parse(expectedCurrentTransactionId),
            expectedCurrentPlanId);

        var result = HushVotingLicencePayloadShapeGuard.Validate(upgradeMissingPrecondition);

        result.IsValid.Should().BeFalse();
        result.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.ExpectedCurrentInvalid);
    }

    [Theory]
    [InlineData("upgrade_unknown")]
    [InlineData("automatic_upgrade")]
    [InlineData("baseline free")]
    [InlineData("")]
    [InlineData("   ")]
    public void Unknown_or_blank_intent_fails_closed_with_intent_unknown(string intent)
    {
        // Plan/catalogue values are shape-valid; only the unknown/blank intent must fail
        // closed at the shape boundary (target-plan semantics are Phase 3 catalogue checks).
        var payload = new HushVotingLicenceAssignmentPayload(
            intent, "hushvoting.direct.free", "hushvoting-licence-catalogue/v1.0.0");
        var result = HushVotingLicencePayloadShapeGuard.Validate(payload);

        result.IsValid.Should().BeFalse();
        result.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.IntentUnknown);
    }

    [Fact]
    public void Shape_guard_is_intent_agnostic_to_target_plan_value()
    {
        // A structurally valid baseline may name any bounded plan id at the shape layer;
        // Enterprise/unknown/retired availability is decided by Phase 3 catalogue checks.
        var payload = new HushVotingLicenceAssignmentPayload(
            HushVotingLicenceTransitionIntent.BaselineFree,
            "hushvoting.enterprise",
            "hushvoting-licence-catalogue/v1.0.0");

        var result = HushVotingLicencePayloadShapeGuard.Validate(payload);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Over_bounded_plan_id_fails_closed_as_malformed()
    {
        var overLongPlanId = new string('a', HushVotingLicencePayloadBounds.MaxPlanIdUtf8Bytes + 1);
        var payload = new HushVotingLicenceAssignmentPayload(
            HushVotingLicenceTransitionIntent.BaselineFree, overLongPlanId, CatalogueV1);

        var result = HushVotingLicencePayloadShapeGuard.Validate(payload);

        result.IsValid.Should().BeFalse();
        result.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.PayloadMalformed);
    }

    [Fact]
    public void Over_bounded_catalogue_version_fails_closed_as_malformed()
    {
        var overLongVersion = new string('v', HushVotingLicencePayloadBounds.MaxCatalogueVersionUtf8Bytes + 1);
        var payload = new HushVotingLicenceAssignmentPayload(
            HushVotingLicenceTransitionIntent.BaselineFree, DirectFree, overLongVersion);

        var result = HushVotingLicencePayloadShapeGuard.Validate(payload);

        result.IsValid.Should().BeFalse();
        result.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.PayloadMalformed);
    }

    [Fact]
    public void Over_bounded_expected_current_plan_fails_closed()
    {
        var overLongPlanId = new string('a', HushVotingLicencePayloadBounds.MaxPlanIdUtf8Bytes + 1);
        var payload = new HushVotingLicenceAssignmentPayload(
            HushVotingLicenceTransitionIntent.ConfirmedUpgrade,
            Veritas2000,
            CatalogueV1,
            CanonicalCurrentTransaction,
            overLongPlanId);

        var result = HushVotingLicencePayloadShapeGuard.Validate(payload);

        result.IsValid.Should().BeFalse();
        result.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.ExpectedCurrentInvalid);
    }
}
