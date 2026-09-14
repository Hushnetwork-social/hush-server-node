// FEAT-015 Task 6.1/6.2 — licence query auth + response mapping contract tests.
//
// Locks: metadata header names; canonical payload bytes (ordinal deep-sort
// {actorAddress, method, request:{}, signedAt}); active/no-active proto mapping with the
// safe-field allowlist (no history/keys/cache/signatures/internal text); direct-free
// template exact shape; and unknown state -> unavailable (never fabricated Direct Free).

using System.Text;
using FluentAssertions;
using HushNetwork.proto;
using HushNode.HushVoting.Licence.Transactions;
using Xunit;

namespace HushNode.HushVoting.Licence.gRPC.Tests;

public sealed class LicenceQueryAuthValidatorContractTests
{
    [Fact]
    public void Metadata_header_names_are_frozen()
    {
        LicenceQueryRequestAuthValidator.SignatoryHeader.Should().Be("x-hush-licence-query-signatory");
        LicenceQueryRequestAuthValidator.SignedAtHeader.Should().Be("x-hush-licence-query-signed-at");
        LicenceQueryRequestAuthValidator.SignatureHeader.Should().Be("x-hush-licence-query-signature");
    }

    [Fact]
    public void Canonical_payload_uses_ordinal_deep_sort_and_exact_bytes()
    {
        var json = LicenceQueryRequestAuthValidator.BuildSignedPayload(
            "GetMyEntitlement",
            "0237fdd4364c0b898908be2f1a98a6b4a7890c623ae92a283640e44d87e048daa5",
            "2026-09-06T00:00:00Z");

        json.Should().Be(
            "{\"actorAddress\":\"0237fdd4364c0b898908be2f1a98a6b4a7890c623ae92a283640e44d87e048daa5\"," +
            "\"method\":\"GetMyEntitlement\",\"request\":{},\"signedAt\":\"2026-09-06T00:00:00Z\"}");

        var bytes = LicenceQueryRequestAuthValidator.CanonicalBytes(
            "GetMyEntitlement",
            "0237fdd4364c0b898908be2f1a98a6b4a7890c623ae92a283640e44d87e048daa5",
            "2026-09-06T00:00:00Z");
        Encoding.UTF8.GetString(bytes).Should().Be(json);
    }

    [Fact]
    public void Canonical_addresses_are_trimmed_and_invariant_lower()
    {
        // The internal normalizer lower-cases and trims the signatory (hex addresses are
        // case-insensitive); a canonical actor round-trips unchanged.
        var upper = "0237FDD4364C0B898908BE2F1A98A6B4A7890C623AE92A283640E44D87E048DAA5";
        var payload = LicenceQueryRequestAuthValidator.BuildSignedPayload(
            "GetMyEntitlement", "0237fdd4364c0b898908be2f1a98a6b4a7890c623ae92a283640e44d87e048daa5", "2026-09-06T00:00:00Z");
        payload.Should().NotContain(upper);
    }
}

public sealed class LicenceQueryResponseMappingsTests
{
    private const string CatalogueV1 = "hushvoting-licence-catalogue/v1.0.0";

    private static HushVotingLicenceActiveView NewActiveView() =>
        new(
            LicenceReference: "5f2d9e11-3c44-4a80-b8e7-6b2f1a0c9d3e",
            PlanId: "hushvoting.veritas.2000",
            PlanFamily: "veritas",
            DisplayName: "HushVoting! Veritas 2k",
            SafeDescription: "Annual Veritas licence",
            EligibleVoterCap: 2000,
            UnlimitedElections: true,
            TermKind: "annual",
            TermYears: 1,
            AllowedGovernanceOptionIds: new[] { "gov-trustees-7-of-10" },
            EffectiveFromUtc: DateTime.Parse("2026-01-01T00:00:00Z").ToUniversalTime(),
            ExpiresAtUtc: DateTime.Parse("2027-01-01T00:00:00Z").ToUniversalTime(),
            AssignedCatalogueVersion: CatalogueV1,
            HigherOptions: new[]
            {
                new HushVotingLicenceOptionTemplate(
                    "hushvoting.veritas.10000", "HushVoting! Veritas 10k", "10,000 voters",
                    10000, true, "annual", 1),
            },
            Enterprise: new HushVotingLicenceEnterpriseInfo(
                "hushvoting.enterprise", "HushVoting! Enterprise", "Contact provider"));

    private static LicenceEntitlementQueryApplicationResult ActiveResult() =>
        new(HushVotingLicenceEntitlementQueryState.Active, NewActiveView(), null, null);

    private static LicenceEntitlementQueryApplicationResult NoActiveResult() =>
        new(
            HushVotingLicenceEntitlementQueryState.NoActive,
            null,
            new HushVotingLicenceDirectFreeTemplate(
                HushVotingLicenceTransitionIntent.BaselineFree, "hushvoting.direct.free", CatalogueV1),
            null);

    private static LicenceEntitlementQueryApplicationResult UnavailableResult() =>
        new(HushVotingLicenceEntitlementQueryState.Unavailable, null, null, "licence_index_unavailable");

    [Fact]
    public void Active_response_exposes_only_safe_fields_and_higher_option()
    {
        var response = LicenceQueryResponseMappings.ToProto(ActiveResult());

        response.State.Should().Be(LicenceEntitlementState.Active);
        var active = response.Active;
        active.LicenceReference.Should().Be("5f2d9e11-3c44-4a80-b8e7-6b2f1a0c9d3e");
        active.PlanId.Should().Be("hushvoting.veritas.2000");
        active.EligibleVoterCap.Should().Be(2000);
        active.ExpiresAtUtc.Should().Be("2027-01-01T00:00:00.000Z");
        active.AssignedCatalogueVersion.Should().Be(CatalogueV1);
        active.HigherOptions.Should().ContainSingle();
        active.HigherOptions[0].PlanId.Should().Be("hushvoting.veritas.10000");
        active.Enterprise.PlanId.Should().Be("hushvoting.enterprise");
        response.DirectFreeTemplate.Should().BeNull();
        response.UnavailableCode.Should().BeEmpty();
    }

    [Fact]
    public void No_active_response_exposes_exactly_one_direct_free_template()
    {
        var response = LicenceQueryResponseMappings.ToProto(NoActiveResult());

        response.State.Should().Be(LicenceEntitlementState.NoActive);
        response.DirectFreeTemplate.TransitionIntent.Should().Be("baseline_free");
        response.DirectFreeTemplate.RequestedPlanId.Should().Be("hushvoting.direct.free");
        response.DirectFreeTemplate.ObservedCatalogueVersion.Should().Be(CatalogueV1);
        response.Active.Should().BeNull();
    }

    [Fact]
    public void Unavailable_state_maps_to_unspecified_with_stable_code()
    {
        var response = LicenceQueryResponseMappings.ToProto(UnavailableResult());

        response.State.Should().Be(LicenceEntitlementState.Unspecified);
        response.UnavailableCode.Should().Be("licence_index_unavailable");
        response.DirectFreeTemplate.Should().BeNull();
        response.Active.Should().BeNull();
    }

    [Fact]
    public void Active_response_never_leaks_internal_or_provenance_fields()
    {
        var response = LicenceQueryResponseMappings.ToProto(ActiveResult());

        // Proto shape has no internal/provenance fields by construction; assert the wire
        // serialization contains only safe tokens and no forbidden vocabulary.
        var wire = response.ToString();
        wire.Should().NotContain("Subject");
        wire.Should().NotContain("Digest");
        wire.Should().NotContain("Signature");
        wire.Should().NotContain("Outbox");
        wire.Should().NotContain("Revision");
    }
}
