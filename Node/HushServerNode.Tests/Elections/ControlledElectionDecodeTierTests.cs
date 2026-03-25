using System.Collections.Immutable;
using System.Numerics;
using FluentAssertions;
using HushServerNode.Testing.Elections;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class ControlledElectionDecodeTierTests
{
    [Fact]
    public void TryDecodeReleasedSelections_ShouldSucceedForDevSmokeTier()
    {
        // Arrange
        var counts = ImmutableArray.Create<BigInteger>(64, 3, 0, 0, 1, 0);

        // Act
        var decodeResult = ReleaseAndDecode(counts, ControlledElectionDecodeTiers.DevSmoke, seed: 10101);

        // Assert
        decodeResult.IsSuccessful.Should().BeTrue();
        decodeResult.DecodedCounts.Should().Equal(counts);
        decodeResult.SupportedUpperBound.Should().Be(ControlledElectionDecodeTiers.DevSmoke);
    }

    [Fact]
    public void TryDecodeReleasedSelections_ShouldSucceedForClubRolloutTier()
    {
        // Arrange
        var counts = ImmutableArray.Create<BigInteger>(5000, 1234, 0, 0, 77, 0);

        // Act
        var decodeResult = ReleaseAndDecode(counts, ControlledElectionDecodeTiers.ClubRollout, seed: 11101);

        // Assert
        decodeResult.IsSuccessful.Should().BeTrue();
        decodeResult.DecodedCounts.Should().Equal(counts);
        decodeResult.SupportedUpperBound.Should().Be(ControlledElectionDecodeTiers.ClubRollout);
    }

    [Fact]
    public void TryDecodeReleasedSelections_ShouldSucceedForUpperSupportedTier()
    {
        // Arrange
        var counts = ImmutableArray.Create<BigInteger>(20000, 19999, 17, 0, 0, 1);

        // Act
        var decodeResult = ReleaseAndDecode(counts, ControlledElectionDecodeTiers.UpperSupported, seed: 12101);

        // Assert
        decodeResult.IsSuccessful.Should().BeTrue();
        decodeResult.DecodedCounts.Should().Equal(counts);
        decodeResult.SupportedUpperBound.Should().Be(ControlledElectionDecodeTiers.UpperSupported);
    }

    [Fact]
    public void TryDecodeReleasedSelections_AboveSupportedBound_ShouldFailExplicitly()
    {
        // Arrange
        var counts = ImmutableArray.Create<BigInteger>(ControlledElectionDecodeTiers.UpperSupported + 1, 0, 0, 0, 0, 0);

        // Act
        var decodeResult = ReleaseAndDecode(counts, ControlledElectionDecodeTiers.UpperSupported, seed: 13101);

        // Assert
        decodeResult.IsSuccessful.Should().BeFalse();
        decodeResult.FailureCode.Should().Be("DECODE_BOUND_EXCEEDED");
        decodeResult.Notes.Should().Contain("within bound");
    }

    private static ControlledElectionDecodeResult ReleaseAndDecode(
        ImmutableArray<BigInteger> counts,
        BigInteger maxSupportedCount,
        BigInteger seed)
    {
        var thresholdDefinition = new ControlledElectionThresholdDefinition(
            ElectionId: $"election-decode-{seed}",
            TrusteeIds: ImmutableArray.Create(
                "trustee-a",
                "trustee-b",
                "trustee-c",
                "trustee-d",
                "trustee-e"),
            Threshold: 3);
        var thresholdSetup = ControlledElectionHarness.CreateControlledThresholdSetup(
            thresholdDefinition,
            sessionId: $"session-decode-{seed}",
            targetTallyId: $"tally-decode-{seed}",
            seed: seed);
        var tally = ControlledElectionHarness.CreateProtectedTallyFromCounts(
            electionId: thresholdDefinition.ElectionId,
            counts: counts,
            publicKey: thresholdSetup.PublicKey,
            nonces: ControlledElectionHarness.CreateDeterministicNonceSequence(
                seed + 100,
                counts.Length));
        var releaseAttempt = new ControlledElectionReleaseAttempt(
            thresholdDefinition,
            SessionId: $"session-decode-{seed}",
            TargetTallyId: $"tally-decode-{seed}",
            SubmittedShares: ImmutableArray.Create(
                thresholdSetup.Shares[0],
                thresholdSetup.Shares[2],
                thresholdSetup.Shares[4]));
        var releaseResult = ControlledElectionHarness.TryReleaseProtectedTally(
            thresholdSetup,
            releaseAttempt,
            tally);

        releaseResult.IsSuccessful.Should().BeTrue("controlled decode tiers are evaluated from a released tally");

        return ControlledElectionHarness.TryDecodeReleasedSelections(
            releaseResult.ReleasedSelections,
            maxSupportedCount);
    }
}
