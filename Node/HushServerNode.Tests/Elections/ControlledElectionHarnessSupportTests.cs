using System.Collections.Immutable;
using System.Numerics;
using FluentAssertions;
using HushServerNode.Testing.Elections;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class ControlledElectionHarnessSupportTests
{
    [Fact]
    public void CreateDeterministicKeyPair_WithSameSeed_ShouldReturnSameKeyMaterial()
    {
        // Arrange
        const int Seed = 17;

        // Act
        var firstKeyPair = ControlledElectionHarness.CreateDeterministicKeyPair(Seed);
        var secondKeyPair = ControlledElectionHarness.CreateDeterministicKeyPair(Seed);

        // Assert
        firstKeyPair.PrivateKey.Should().Be(secondKeyPair.PrivateKey);
        firstKeyPair.PublicKey.Should().BeEquivalentTo(secondKeyPair.PublicKey);
    }

    [Fact]
    public void EncryptOneHotBallot_WithDeterministicInputs_ShouldCreateReplayableBallot()
    {
        // Arrange
        var keyPair = ControlledElectionHarness.CreateDeterministicKeyPair(101);
        var nonces = ControlledElectionHarness.CreateDeterministicNonceSequence(200, ControlledElectionHarness.DefaultSelectionCount);

        // Act
        var ballot = ControlledElectionHarness.EncryptOneHotBallot(
            ballotId: "ballot-001",
            choiceIndex: 2,
            publicKey: keyPair.PublicKey,
            nonces: nonces);
        var validation = ControlledElectionHarness.ValidateBallot(
            ballot,
            ControlledElectionHarness.DefaultSelectionCount);

        // Assert
        validation.IsValid.Should().BeTrue();
        ballot.BallotId.Should().Be("ballot-001");
        ballot.Slots.Should().HaveCount(ControlledElectionHarness.DefaultSelectionCount);
        ballot.Slots.Should().OnlyContain(slot => slot.C1 != slot.C2);
    }

    [Fact]
    public void TryDecryptBallotForHarness_WithWrongPrivateKey_ShouldNotRecoverOriginalMeaning()
    {
        // Arrange
        var correctKeyPair = ControlledElectionHarness.CreateDeterministicKeyPair(111);
        var wrongKeyPair = ControlledElectionHarness.CreateDeterministicKeyPair(222);
        var ballot = ControlledElectionHarness.EncryptOneHotBallot(
            ballotId: "ballot-wrong-key",
            choiceIndex: 2,
            publicKey: correctKeyPair.PublicKey,
            nonces: ControlledElectionHarness.CreateDeterministicNonceSequence(210, ControlledElectionHarness.DefaultSelectionCount));

        // Act
        var result = ControlledElectionHarness.TryDecryptBallotForHarness(
            ballot,
            wrongKeyPair.PrivateKey,
            maxSupportedCount: 1);

        // Assert
        result.IsSuccessful.Should().BeFalse();
        result.FailureCode.Should().Be("DECODE_BOUND_EXCEEDED");
    }

    [Fact]
    public void CreateSupportRecords_ShouldExpressTallyShareAndReleaseConceptsDirectly()
    {
        // Arrange
        var thresholdDefinition = new ControlledElectionThresholdDefinition(
            ElectionId: "election-001",
            TrusteeIds: ImmutableArray.Create("trustee-a", "trustee-b", "trustee-c"),
            Threshold: 2);
        var shares = ImmutableArray.Create(
            new ControlledElectionTrusteeShare("election-001", "session-001", "tally-001", "trustee-a", 1, "101"),
            new ControlledElectionTrusteeShare("election-001", "session-001", "tally-001", "trustee-b", 2, "202"));

        // Act
        var releaseAttempt = new ControlledElectionReleaseAttempt(
            thresholdDefinition,
            SessionId: "session-001",
            TargetTallyId: "tally-001",
            SubmittedShares: shares);

        // Assert
        releaseAttempt.ThresholdDefinition.Threshold.Should().Be(2);
        releaseAttempt.SubmittedShares.Should().HaveCount(2);
        releaseAttempt.TargetTallyId.Should().Be("tally-001");
        releaseAttempt.SubmittedShares.Select(share => share.ShareIndex)
            .Should().Contain(new[] { 1, 2 });
        releaseAttempt.SubmittedShares.Select(share => share.TrusteeId)
            .Should().Contain(new[] { "trustee-a", "trustee-b" });
    }
}
