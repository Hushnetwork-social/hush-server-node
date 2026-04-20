using Xunit;
using System.Collections.Immutable;
using FluentAssertions;
using HushServerNode.Testing.Elections;

namespace HushServerNode.Tests.Elections;

[Trait("Category", "FEAT-105")]
public sealed class ControlledElectionTallyReplayTests
{
    [Fact]
    public void AccumulateBallots_WithDifferentReplayOrders_ShouldProduceEquivalentEncryptedTallyState()
    {
        // Arrange
        var keyPair = ControlledElectionHarness.CreateDeterministicKeyPair(77);
        var firstBallot = ControlledElectionHarness.EncryptOneHotBallot(
            ballotId: "ballot-1",
            choiceIndex: 0,
            publicKey: keyPair.PublicKey,
            nonces: ControlledElectionHarness.CreateDeterministicNonceSequence(100, ControlledElectionHarness.DefaultSelectionCount));
        var secondBallot = ControlledElectionHarness.EncryptOneHotBallot(
            ballotId: "ballot-2",
            choiceIndex: 2,
            publicKey: keyPair.PublicKey,
            nonces: ControlledElectionHarness.CreateDeterministicNonceSequence(200, ControlledElectionHarness.DefaultSelectionCount));
        var thirdBallot = ControlledElectionHarness.EncryptOneHotBallot(
            ballotId: "ballot-3",
            choiceIndex: 2,
            publicKey: keyPair.PublicKey,
            nonces: ControlledElectionHarness.CreateDeterministicNonceSequence(300, ControlledElectionHarness.DefaultSelectionCount));

        var originalOrder = ImmutableArray.Create(firstBallot, secondBallot, thirdBallot);
        var replayOrder = ImmutableArray.Create(thirdBallot, firstBallot, secondBallot);

        // Act
        var originalTally = ControlledElectionHarness.AccumulateBallots("election-001", originalOrder);
        var replayedTally = ControlledElectionHarness.AccumulateBallots("election-001", replayOrder);

        // Assert
        ControlledElectionHarness.CreateTallyFingerprint(originalTally)
            .Should().Be(ControlledElectionHarness.CreateTallyFingerprint(replayedTally));
        originalTally.Slots.Should().BeEquivalentTo(replayedTally.Slots);
    }
}
