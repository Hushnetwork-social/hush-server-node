using FluentAssertions;
using HushNode.HushVoting.Licensing.Storage;
using HushShared.HushVoting.Licensing.Model;
using Xunit;

namespace HushNode.HushVoting.Licensing.Storage.Tests;

/// <summary>
/// FEAT-013 Task 3.6 unit coverage for the higher-plan activation contract: bounded command
/// validation, durable-result round-trips, and closed outcome names.
/// </summary>
public sealed class LicenceActivationContractUnitTests
{
    [Fact]
    public void TryCreate_builds_a_bounded_activation_command()
    {
        var created = LicenceActivationCommand.TryCreate(
            Guid.NewGuid(),
            HushVotingLicencePlanId.DirectFree.Value,
            1,
            HushVotingLicencePlanId.Veritas500.Value,
            "corr-123",
            out var command,
            out var error);

        created.Should().BeTrue();
        error.Should().BeNull();
        command.Should().NotBeNull();
        command!.ExpectedCurrentPlanId.Should().Be(HushVotingLicencePlanId.DirectFree.Value);
        command.ExpectedEntitlementRevision.Should().Be(1);
        command.RequestedTargetPlanId.Should().Be(HushVotingLicencePlanId.Veritas500.Value);
        command.RequestCorrelationId.Should().Be("corr-123");
    }

    [Fact]
    public void TryCreate_rejects_empty_idempotency_key()
    {
        LicenceActivationCommand.TryCreate(
                Guid.Empty,
                HushVotingLicencePlanId.DirectFree.Value,
                1,
                HushVotingLicencePlanId.Veritas500.Value,
                null,
                out _,
                out var error)
            .Should().BeFalse();
        error.Should().Be(LicenceActivationCommand.ErrorInvalidIdempotencyKey);
    }

    [Fact]
    public void TryCreate_rejects_blank_or_oversized_plan_ids()
    {
        LicenceActivationCommand.TryCreate(
                Guid.NewGuid(),
                "   ",
                1,
                HushVotingLicencePlanId.Veritas500.Value,
                null,
                out _,
                out var error)
            .Should().BeFalse();
        error.Should().Be(LicenceActivationCommand.ErrorInvalidExpectedPlan);

        LicenceActivationCommand.TryCreate(
                Guid.NewGuid(),
                HushVotingLicencePlanId.DirectFree.Value,
                1,
                new string('x', 65),
                null,
                out _,
                out var error2)
            .Should().BeFalse();
        error2.Should().Be(LicenceActivationCommand.ErrorInvalidTargetPlan);
    }

    [Fact]
    public void TryCreate_rejects_negative_revision_and_oversized_correlation()
    {
        LicenceActivationCommand.TryCreate(
                Guid.NewGuid(),
                HushVotingLicencePlanId.DirectFree.Value,
                -1,
                HushVotingLicencePlanId.Veritas500.Value,
                null,
                out _,
                out var error)
            .Should().BeFalse();
        error.Should().Be(LicenceActivationCommand.ErrorNegativeExpectedRevision);

        LicenceActivationCommand.TryCreate(
                Guid.NewGuid(),
                HushVotingLicencePlanId.DirectFree.Value,
                1,
                HushVotingLicencePlanId.Veritas500.Value,
                new string('c', 97),
                out _,
                out var error2)
            .Should().BeFalse();
        error2.Should().Be(LicenceActivationCommand.ErrorCorrelationTooLong);
    }

    [Fact]
    public void Activation_wire_names_and_durable_round_trip_are_closed()
    {
        LicenceEntitlementOutcomeNames.ToWireName(LicenceActivationOutcome.Activated).Should().Be("activated");
        LicenceEntitlementOutcomeNames.ToWireName(LicenceActivationOutcome.PlanUnavailable).Should().Be("plan_unavailable");
        LicenceEntitlementOutcomeNames.ToWireName(LicenceActivationOutcome.IdempotencyPayloadMismatch).Should().Be("idempotency_payload_mismatch");

        foreach (var durable in new[]
                 {
                     LicenceActivationOutcome.Activated,
                     LicenceActivationOutcome.TransitionUnchanged,
                     LicenceActivationOutcome.TransitionNotHigher,
                     LicenceActivationOutcome.PlanUnknown,
                     LicenceActivationOutcome.PlanUnavailable,
                     LicenceActivationOutcome.PreconditionConflict,
                     LicenceActivationOutcome.EntitlementNotInitialized,
                 })
        {
            var wire = LicenceEntitlementOutcomeNames.ToWireName(durable);
            LicenceEntitlementOutcomeNames.FromDurableResultString(wire).Should().Be(durable);
        }

        var act = () => LicenceEntitlementOutcomeNames.FromDurableResultString("not-a-durable-result");
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Durable_operation_result_vocabulary_is_exact()
    {
        LicencePersistenceVocabulary.OperationResultActivated.Should().Be("activated");
        LicencePersistenceVocabulary.OperationResultTransitionUnchanged.Should().Be("transition_unchanged");
        LicencePersistenceVocabulary.OperationResultTransitionNotHigher.Should().Be("transition_not_higher");
        LicencePersistenceVocabulary.OperationResultPlanUnknown.Should().Be("plan_unknown");
        LicencePersistenceVocabulary.OperationResultPlanUnavailable.Should().Be("plan_unavailable");
        LicencePersistenceVocabulary.OperationResultPreconditionConflict.Should().Be("precondition_conflict");
        LicencePersistenceVocabulary.OperationResultEntitlementNotInitialized.Should().Be("entitlement_not_initialized");
    }
}
