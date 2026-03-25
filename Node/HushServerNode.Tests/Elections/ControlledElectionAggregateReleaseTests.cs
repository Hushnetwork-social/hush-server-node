using System.Collections.Immutable;
using System.Numerics;
using FluentAssertions;
using HushServerNode.Testing.Elections;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class ControlledElectionAggregateReleaseTests
{
    [Fact]
    public void TryReleaseProtectedTally_ShouldReleaseAndDecodeAggregateCountsWithoutSingleBallotPath()
    {
        // Arrange
        var thresholdDefinition = CreateThresholdDefinition();
        var thresholdSetup = ControlledElectionHarness.CreateControlledThresholdSetup(
            thresholdDefinition,
            sessionId: "session-aggregate-001",
            targetTallyId: "tally-aggregate-001",
            seed: 9101);
        var releaseAttempt = new ControlledElectionReleaseAttempt(
            thresholdDefinition,
            SessionId: "session-aggregate-001",
            TargetTallyId: "tally-aggregate-001",
            SubmittedShares: ImmutableArray.Create(
                thresholdSetup.Shares[0],
                thresholdSetup.Shares[2],
                thresholdSetup.Shares[4]));
        var tally = ControlledElectionHarness.CreateProtectedTallyFromCounts(
            electionId: thresholdDefinition.ElectionId,
            counts: ImmutableArray.Create<BigInteger>(2, 0, 1, 0, 0, 0),
            publicKey: thresholdSetup.PublicKey,
            nonces: ControlledElectionHarness.CreateDeterministicNonceSequence(
                9201,
                ControlledElectionHarness.DefaultSelectionCount));

        // Act
        var releaseResult = ControlledElectionHarness.TryReleaseProtectedTally(
            thresholdSetup,
            releaseAttempt,
            tally);
        var decodeResult = ControlledElectionHarness.TryDecodeReleasedSelections(
            releaseResult.ReleasedSelections,
            maxSupportedCount: 3);

        // Assert
        releaseResult.IsSuccessful.Should().BeTrue();
        decodeResult.IsSuccessful.Should().BeTrue();
        decodeResult.DecodedCounts.Should().Equal(new BigInteger[] { 2, 0, 1, 0, 0, 0 });
    }

    [Fact]
    public void TryReleaseProtectedBallot_ShouldRefuseSingleBallotTargets()
    {
        // Arrange
        var thresholdDefinition = CreateThresholdDefinition();
        var thresholdSetup = ControlledElectionHarness.CreateControlledThresholdSetup(
            thresholdDefinition,
            sessionId: "session-aggregate-002",
            targetTallyId: "tally-aggregate-002",
            seed: 9301);
        var ballot = ControlledElectionHarness.EncryptOneHotBallot(
            ballotId: "ballot-single-target",
            choiceIndex: 2,
            publicKey: thresholdSetup.PublicKey,
            nonces: ControlledElectionHarness.CreateDeterministicNonceSequence(
                9401,
                ControlledElectionHarness.DefaultSelectionCount));
        var releaseAttempt = new ControlledElectionReleaseAttempt(
            thresholdDefinition,
            SessionId: "session-aggregate-002",
            TargetTallyId: "ballot-single-target",
            SubmittedShares: ImmutableArray.Create(
                thresholdSetup.Shares[0],
                thresholdSetup.Shares[1],
                thresholdSetup.Shares[3]));

        // Act
        var releaseResult = ControlledElectionHarness.TryReleaseProtectedBallot(
            thresholdSetup,
            releaseAttempt,
            ballot);

        // Assert
        releaseResult.IsSuccessful.Should().BeFalse();
        releaseResult.FailureCode.Should().Be("SINGLE_BALLOT_RELEASE_FORBIDDEN");
        releaseResult.ReleasedSelections.Should().BeEmpty();
    }

    [Fact]
    public void ValidateAggregateOnlyCountingPath_WhenIndividualBallotDecryptionIsRequired_ShouldReject()
    {
        // Act
        var result = ControlledElectionHarness.ValidateAggregateOnlyCountingPath(
            requiresIndividualBallotDecryption: true);

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureCode.Should().Be("SINGLE_BALLOT_DECRYPTION_FORBIDDEN");
    }

    private static ControlledElectionThresholdDefinition CreateThresholdDefinition() =>
        new(
            ElectionId: "election-aggregate-release",
            TrusteeIds: ImmutableArray.Create(
                "trustee-a",
                "trustee-b",
                "trustee-c",
                "trustee-d",
                "trustee-e"),
            Threshold: 3);
}
