using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using FluentAssertions;
using HushNode.Reactions.Crypto;
using HushServerNode.Testing.Elections;
using HushShared.Reactions.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class ControlledElectionThresholdTests
{
    [Fact]
    public void TryReleaseProtectedTally_WithThresholdShares_ShouldSucceedAndRevealExpectedSelections()
    {
        // Arrange
        var curve = new BabyJubJubCurve();
        var thresholdDefinition = CreateThresholdDefinition();
        var thresholdSetup = ControlledElectionHarness.CreateControlledThresholdSetup(
            thresholdDefinition,
            sessionId: "session-001",
            targetTallyId: "tally-001",
            seed: 901,
            curve);
        var tally = CreateSampleTally(thresholdSetup.PublicKey, curve);
        var releaseAttempt = new ControlledElectionReleaseAttempt(
            thresholdDefinition,
            SessionId: "session-001",
            TargetTallyId: "tally-001",
            SubmittedShares: ImmutableArray.Create(
                thresholdSetup.Shares[0],
                thresholdSetup.Shares[2],
                thresholdSetup.Shares[4]));

        // Act
        var result = ControlledElectionHarness.TryReleaseProtectedTally(
            thresholdSetup,
            releaseAttempt,
            tally,
            curve);

        // Assert
        result.IsSuccessful.Should().BeTrue();
        result.ReleasedSelections.Should().HaveCount(ControlledElectionHarness.DefaultSelectionCount);
        result.ReleasedSelections[0].Should().BeEquivalentTo(curve.ScalarMul(curve.Generator, 1));
        result.ReleasedSelections[2].Should().BeEquivalentTo(curve.ScalarMul(curve.Generator, 2));
        result.ReleasedSelections[1].Should().BeEquivalentTo(curve.Identity);
        result.ReleasedSelections[3].Should().BeEquivalentTo(curve.Identity);
        result.ReleasedSelections[4].Should().BeEquivalentTo(curve.Identity);
        result.ReleasedSelections[5].Should().BeEquivalentTo(curve.Identity);
    }

    [Fact]
    public void TryReleaseProtectedTally_WithTooFewShares_ShouldFail()
    {
        // Arrange
        var thresholdDefinition = CreateThresholdDefinition();
        var thresholdSetup = ControlledElectionHarness.CreateControlledThresholdSetup(
            thresholdDefinition,
            sessionId: "session-002",
            targetTallyId: "tally-002",
            seed: 902);
        var tally = CreateSampleTally(thresholdSetup.PublicKey);
        var releaseAttempt = new ControlledElectionReleaseAttempt(
            thresholdDefinition,
            SessionId: "session-002",
            TargetTallyId: "tally-002",
            SubmittedShares: ImmutableArray.Create(
                thresholdSetup.Shares[0],
                thresholdSetup.Shares[1]));

        // Act
        var result = ControlledElectionHarness.TryReleaseProtectedTally(
            thresholdSetup,
            releaseAttempt,
            tally);

        // Assert
        result.IsSuccessful.Should().BeFalse();
        result.FailureCode.Should().Be("INSUFFICIENT_SHARES");
    }

    [Fact]
    public void TryReleaseProtectedTally_WithDuplicateShares_ShouldFail()
    {
        // Arrange
        var thresholdDefinition = CreateThresholdDefinition();
        var thresholdSetup = ControlledElectionHarness.CreateControlledThresholdSetup(
            thresholdDefinition,
            sessionId: "session-003",
            targetTallyId: "tally-003",
            seed: 903);
        var tally = CreateSampleTally(thresholdSetup.PublicKey);
        var duplicateShare = thresholdSetup.Shares[0];
        var releaseAttempt = new ControlledElectionReleaseAttempt(
            thresholdDefinition,
            SessionId: "session-003",
            TargetTallyId: "tally-003",
            SubmittedShares: ImmutableArray.Create(
                duplicateShare,
                duplicateShare,
                thresholdSetup.Shares[2]));

        // Act
        var result = ControlledElectionHarness.TryReleaseProtectedTally(
            thresholdSetup,
            releaseAttempt,
            tally);

        // Assert
        result.IsSuccessful.Should().BeFalse();
        result.FailureCode.Should().Be("DUPLICATE_SHARE");
    }

    [Fact]
    public void TryReleaseProtectedTally_WithWrongTargetShare_ShouldFail()
    {
        // Arrange
        var thresholdDefinition = CreateThresholdDefinition();
        var thresholdSetup = ControlledElectionHarness.CreateControlledThresholdSetup(
            thresholdDefinition,
            sessionId: "session-004",
            targetTallyId: "tally-004",
            seed: 904);
        var tally = CreateSampleTally(thresholdSetup.PublicKey);
        var wrongTargetShare = thresholdSetup.Shares[1] with { TargetTallyId = "tally-other" };
        var releaseAttempt = new ControlledElectionReleaseAttempt(
            thresholdDefinition,
            SessionId: "session-004",
            TargetTallyId: "tally-004",
            SubmittedShares: ImmutableArray.Create(
                thresholdSetup.Shares[0],
                wrongTargetShare,
                thresholdSetup.Shares[3]));

        // Act
        var result = ControlledElectionHarness.TryReleaseProtectedTally(
            thresholdSetup,
            releaseAttempt,
            tally);

        // Assert
        result.IsSuccessful.Should().BeFalse();
        result.FailureCode.Should().Be("WRONG_TARGET_SHARE");
    }

    [Fact]
    public void TryReleaseProtectedTally_WithMalformedShareMaterial_ShouldFail()
    {
        // Arrange
        var thresholdDefinition = CreateThresholdDefinition();
        var thresholdSetup = ControlledElectionHarness.CreateControlledThresholdSetup(
            thresholdDefinition,
            sessionId: "session-005",
            targetTallyId: "tally-005",
            seed: 905);
        var tally = CreateSampleTally(thresholdSetup.PublicKey);
        var malformedShare = thresholdSetup.Shares[2] with
        {
            ShareMaterial = (BigInteger.Parse(
                thresholdSetup.Shares[2].ShareMaterial,
                CultureInfo.InvariantCulture) + 1)
                .ToString(CultureInfo.InvariantCulture)
        };
        var releaseAttempt = new ControlledElectionReleaseAttempt(
            thresholdDefinition,
            SessionId: "session-005",
            TargetTallyId: "tally-005",
            SubmittedShares: ImmutableArray.Create(
                thresholdSetup.Shares[0],
                thresholdSetup.Shares[1],
                malformedShare));

        // Act
        var result = ControlledElectionHarness.TryReleaseProtectedTally(
            thresholdSetup,
            releaseAttempt,
            tally);

        // Assert
        result.IsSuccessful.Should().BeFalse();
        result.FailureCode.Should().Be("MALFORMED_SHARE");
    }

    private static ControlledElectionThresholdDefinition CreateThresholdDefinition() =>
        new(
            ElectionId: "election-threshold",
            TrusteeIds: ImmutableArray.Create(
                "trustee-a",
                "trustee-b",
                "trustee-c",
                "trustee-d",
                "trustee-e"),
            Threshold: 3);

    private static ControlledElectionTallyState CreateSampleTally(
        ECPoint publicKey,
        IBabyJubJub? curve = null)
    {
        var activeCurve = curve ?? new BabyJubJubCurve();
        var firstBallot = ControlledElectionHarness.EncryptOneHotBallot(
            ballotId: "ballot-1",
            choiceIndex: 0,
            publicKey: publicKey,
            nonces: ControlledElectionHarness.CreateDeterministicNonceSequence(100, ControlledElectionHarness.DefaultSelectionCount, activeCurve),
            curve: activeCurve);
        var secondBallot = ControlledElectionHarness.EncryptOneHotBallot(
            ballotId: "ballot-2",
            choiceIndex: 2,
            publicKey: publicKey,
            nonces: ControlledElectionHarness.CreateDeterministicNonceSequence(200, ControlledElectionHarness.DefaultSelectionCount, activeCurve),
            curve: activeCurve);
        var thirdBallot = ControlledElectionHarness.EncryptOneHotBallot(
            ballotId: "ballot-3",
            choiceIndex: 2,
            publicKey: publicKey,
            nonces: ControlledElectionHarness.CreateDeterministicNonceSequence(300, ControlledElectionHarness.DefaultSelectionCount, activeCurve),
            curve: activeCurve);

        return ControlledElectionHarness.AccumulateBallots(
            "election-threshold",
            ImmutableArray.Create(firstBallot, secondBallot, thirdBallot),
            activeCurve);
    }
}
