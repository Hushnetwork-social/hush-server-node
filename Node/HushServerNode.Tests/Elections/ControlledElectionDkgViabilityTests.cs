using System.Collections.Immutable;
using FluentAssertions;
using HushNode.Reactions.Crypto;
using HushServerNode.Testing.Elections;
using HushShared.Reactions.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class ControlledElectionDkgViabilityTests
{
    [Fact]
    public void SimulateLocalDkgViability_WithSeparateParticipants_ShouldProduceCompatibleThresholdSetup()
    {
        // Arrange
        var curve = new BabyJubJubCurve();
        var thresholdDefinition = new ControlledElectionThresholdDefinition(
            ElectionId: "election-dkg",
            TrusteeIds: ImmutableArray.Create("trustee-a", "trustee-b", "trustee-c", "trustee-d", "trustee-e"),
            Threshold: 3);

        // Act
        var result = ControlledElectionHarness.SimulateLocalDkgViability(
            thresholdDefinition,
            sessionId: "session-dkg",
            targetTallyId: "tally-dkg",
            seed: 1200,
            curve);

        // Assert
        result.Notes.Should().Contain("Exploratory");
        result.ParticipantArtifacts.Should().HaveCount(5);
        result.ParticipantArtifacts.Select(artifact => artifact.TrusteeId)
            .Should().OnlyHaveUniqueItems();
        result.ParticipantArtifacts.Should().OnlyContain(artifact =>
            artifact.OutboundSharePackages.Length == thresholdDefinition.TrusteeIds.Length &&
            artifact.OutboundSharePackages.All(package => package.FromTrusteeId == artifact.TrusteeId));

        var tally = CreateSampleTally(result.ThresholdSetup.PublicKey, curve);
        var releaseAttempt = new ControlledElectionReleaseAttempt(
            thresholdDefinition,
            SessionId: "session-dkg",
            TargetTallyId: "tally-dkg",
            SubmittedShares: ImmutableArray.Create(
                result.ThresholdSetup.Shares[0],
                result.ThresholdSetup.Shares[2],
                result.ThresholdSetup.Shares[4]));
        var release = ControlledElectionHarness.TryReleaseProtectedTally(
            result.ThresholdSetup,
            releaseAttempt,
            tally,
            curve);

        release.IsSuccessful.Should().BeTrue();
        release.ReleasedSelections[0].Should().BeEquivalentTo(curve.ScalarMul(curve.Generator, 1));
        release.ReleasedSelections[2].Should().BeEquivalentTo(curve.ScalarMul(curve.Generator, 2));
    }

    [Fact]
    public void SimulateLocalDkgViability_ShouldKeepOutboundSharePackagesBoundToEachRecipient()
    {
        // Arrange
        var thresholdDefinition = new ControlledElectionThresholdDefinition(
            ElectionId: "election-dkg-bindings",
            TrusteeIds: ImmutableArray.Create("trustee-a", "trustee-b", "trustee-c"),
            Threshold: 2);

        // Act
        var result = ControlledElectionHarness.SimulateLocalDkgViability(
            thresholdDefinition,
            sessionId: "session-bindings",
            targetTallyId: "tally-bindings",
            seed: 1500);

        // Assert
        foreach (var participant in result.ParticipantArtifacts)
        {
            participant.OutboundSharePackages.Select(package => package.ToTrusteeId)
                .Should().BeEquivalentTo(thresholdDefinition.TrusteeIds);
            participant.OutboundSharePackages.Select(package => package.ShareIndex)
                .Should().BeEquivalentTo(new[] { 1, 2, 3 });
        }
    }

    private static ControlledElectionTallyState CreateSampleTally(
        ECPoint publicKey,
        IBabyJubJub? curve = null)
    {
        var activeCurve = curve ?? new BabyJubJubCurve();
        var firstBallot = ControlledElectionHarness.EncryptOneHotBallot(
            ballotId: "ballot-1",
            choiceIndex: 0,
            publicKey: publicKey,
            nonces: ControlledElectionHarness.CreateDeterministicNonceSequence(400, ControlledElectionHarness.DefaultSelectionCount, activeCurve),
            curve: activeCurve);
        var secondBallot = ControlledElectionHarness.EncryptOneHotBallot(
            ballotId: "ballot-2",
            choiceIndex: 2,
            publicKey: publicKey,
            nonces: ControlledElectionHarness.CreateDeterministicNonceSequence(500, ControlledElectionHarness.DefaultSelectionCount, activeCurve),
            curve: activeCurve);
        var thirdBallot = ControlledElectionHarness.EncryptOneHotBallot(
            ballotId: "ballot-3",
            choiceIndex: 2,
            publicKey: publicKey,
            nonces: ControlledElectionHarness.CreateDeterministicNonceSequence(600, ControlledElectionHarness.DefaultSelectionCount, activeCurve),
            curve: activeCurve);

        return ControlledElectionHarness.AccumulateBallots(
            "election-dkg",
            ImmutableArray.Create(firstBallot, secondBallot, thirdBallot),
            activeCurve);
    }
}
