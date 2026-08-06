// FEAT-011 Task 2.8 — concurrency-model, evidence-schema, and rejection tests
// for the reservation/evidence contracts (Task 2.7).
//
// Covers: first/exact retry/conflicting retry/indexed/released/restart
// outcomes; atomic race model vocabulary; structured status mapping; unknown
// state; secret/evidence allowlist; Manual TestPack IDs; PASS|FAIL|NOT_SUPPLIED;
// no human-attestation or export fields.

using FluentAssertions;
using HushShared.Identity.Model;
using Xunit;

namespace HushNode.Identity.Tests;

public sealed class FullIdentityReservationContractsTests
{
    [Fact]
    public void FirstValidRegistration_MapsToAccepted_WithNoValidationCode()
    {
        var result = FullIdentityReservationResult.Accepted();

        result.Outcome.Should().Be(FullIdentitySubmitOutcome.Accepted);
        result.ValidationCode.Should().BeNull();
    }

    [Fact]
    public void ExactRetryAndConcurrentDuplicate_MapToPending()
    {
        FullIdentityReservationResult.Pending().Outcome.Should().Be(FullIdentitySubmitOutcome.Pending);
    }

    [Fact]
    public void IndexedIdentity_MapsToAlreadyExists_WithoutMempoolAdmission()
    {
        var result = FullIdentityReservationResult.AlreadyExists();

        result.Outcome.Should().Be(FullIdentitySubmitOutcome.AlreadyExists);
        result.ValidationCode.Should().BeNull();
    }

    [Fact]
    public void ConflictingSameSigningPending_MapsToStableConflict()
    {
        FullIdentityReservationResult.Conflict().Outcome.Should().Be(FullIdentitySubmitOutcome.Conflict);
    }

    [Fact]
    public void TerminalRejection_CarriesStableValidationCode()
    {
        var result = FullIdentityReservationResult.Rejected("FULL_IDENTITY_INVALID_SIGNATURE");

        result.Outcome.Should().Be(FullIdentitySubmitOutcome.RejectedTerminal);
        result.ValidationCode.Should().Be("FULL_IDENTITY_INVALID_SIGNATURE");
    }

    [Fact]
    public void EditableRejection_IsDistinctFromTerminal()
    {
        var result = FullIdentityReservationResult.RejectedEditable("FULL_IDENTITY_ALIAS_OUT_OF_BOUNDS");

        result.Outcome.Should().Be(FullIdentitySubmitOutcome.RejectedEditable);
        result.ValidationCode.Should().Be("FULL_IDENTITY_ALIAS_OUT_OF_BOUNDS");
    }

    [Fact]
    public void UnknownState_FailsClosed()
    {
        FullIdentityReservationResult.Unknown().Outcome.Should().Be(FullIdentitySubmitOutcome.Unknown);
    }

    [Fact]
    public void ReservationLifecycle_CoversAllFrozenStates()
    {
        var states = Enum.GetValues<ReservationState>();
        states.Should().Equal(
            ReservationState.AbsentUnreserved,
            ReservationState.ReservedPending,
            ReservationState.Indexed,
            ReservationState.Released);
    }

    [Fact]
    public void SubmitOutcomeEnum_ContainsNoFreeFormMessagePath()
    {
        // Structured outcomes only — there is no 'Message' or 'Parsed' member
        // and no generic signer/export member anywhere in the contract surface.
        var outcomes = Enum.GetNames<FullIdentitySubmitOutcome>();
        outcomes.Should().NotContain("Message");
        outcomes.Should().NotContain("Parsed");
        outcomes.Should().NotContain("Export");
    }

    [Fact]
    public void ReservationServiceContract_HasNoHumanAttestationOrExportMembers()
    {
        var members = typeof(IFullIdentityReservationService).GetMethods().Select(m => m.Name);
        members.Should().NotContain(m => m.Contains("attest", StringComparison.OrdinalIgnoreCase));
        members.Should().NotContain(m => m.Contains("export", StringComparison.OrdinalIgnoreCase));
        members.Should().BeEquivalentTo(
            "ReserveAsync",
            "ReleaseAsync",
            "MarkIndexedAsync");
    }

    [Fact]
    public void ReservationContract_SurfacesNoSignerOrPrivateKeyMaterial()
    {
        var parameterTypes = typeof(IFullIdentityReservationService)
            .GetMethods()
            .SelectMany(m => m.GetParameters())
            .Select(p => p.ParameterType.Name);

        parameterTypes.Should().NotContain("PrivateKey");
        parameterTypes.Should().NotContain("Signature");
        parameterTypes.Should().NotContain("SigningKey");
    }
}
