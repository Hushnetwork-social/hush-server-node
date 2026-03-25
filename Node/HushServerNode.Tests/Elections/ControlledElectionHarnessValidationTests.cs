using System.Collections.Immutable;
using System.Numerics;
using FluentAssertions;
using HushServerNode.Testing.Elections;
using HushShared.Reactions.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class ControlledElectionHarnessValidationTests
{
    [Fact]
    public void ValidatePublicKey_WithIdentityPoint_ShouldFail()
    {
        // Arrange
        var identityKey = new ECPoint(BigInteger.Zero, BigInteger.One);

        // Act
        var result = ControlledElectionHarness.ValidatePublicKey(identityKey);

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureCode.Should().Be("INVALID_PUBLIC_KEY");
    }

    [Fact]
    public void ValidatePublicKey_WithOffCurvePoint_ShouldFail()
    {
        // Arrange
        var keyPair = ControlledElectionHarness.CreateDeterministicKeyPair(22);
        var offCurvePoint = new ECPoint(keyPair.PublicKey.X, keyPair.PublicKey.Y + 1);

        // Act
        var result = ControlledElectionHarness.ValidatePublicKey(offCurvePoint);

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureCode.Should().Be("INVALID_PUBLIC_KEY");
    }

    [Fact]
    public void ValidateBallot_WithOffCurveCiphertextPoint_ShouldFail()
    {
        // Arrange
        var keyPair = ControlledElectionHarness.CreateDeterministicKeyPair(31);
        var ballot = ControlledElectionHarness.EncryptOneHotBallot(
            ballotId: "ballot-invalid",
            choiceIndex: 1,
            publicKey: keyPair.PublicKey,
            nonces: ControlledElectionHarness.CreateDeterministicNonceSequence(400, ControlledElectionHarness.DefaultSelectionCount));
        var tamperedSlots = ballot.Slots.SetItem(
            0,
            ballot.Slots[0] with
            {
                C1 = new ECPoint(ballot.Slots[0].C1.X, ballot.Slots[0].C1.Y + 1)
            });
        var tamperedBallot = ballot with { Slots = tamperedSlots };

        // Act
        var result = ControlledElectionHarness.ValidateBallot(tamperedBallot, ControlledElectionHarness.DefaultSelectionCount);

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureCode.Should().Be("INVALID_CIPHERTEXT_STRUCTURE");
    }

    [Fact]
    public void ValidateThresholdDefinition_WithInvalidConfigurations_ShouldFail()
    {
        // Arrange
        var zeroThreshold = new ControlledElectionThresholdDefinition(
            "election-zero",
            ImmutableArray.Create("trustee-a", "trustee-b"),
            0);
        var aboveCountThreshold = new ControlledElectionThresholdDefinition(
            "election-high",
            ImmutableArray.Create("trustee-a", "trustee-b"),
            3);
        var duplicateTrustees = new ControlledElectionThresholdDefinition(
            "election-duplicate",
            ImmutableArray.Create("trustee-a", "trustee-a", "trustee-b"),
            2);

        // Act
        var zeroResult = ControlledElectionHarness.ValidateThresholdDefinition(zeroThreshold);
        var aboveCountResult = ControlledElectionHarness.ValidateThresholdDefinition(aboveCountThreshold);
        var duplicateResult = ControlledElectionHarness.ValidateThresholdDefinition(duplicateTrustees);

        // Assert
        zeroResult.IsValid.Should().BeFalse();
        aboveCountResult.IsValid.Should().BeFalse();
        duplicateResult.IsValid.Should().BeFalse();
        zeroResult.FailureCode.Should().Be("INVALID_THRESHOLD_CONFIGURATION");
        aboveCountResult.FailureCode.Should().Be("INVALID_THRESHOLD_CONFIGURATION");
        duplicateResult.FailureCode.Should().Be("INVALID_THRESHOLD_CONFIGURATION");
    }

    [Fact]
    public void ValidateNonceSequence_WithUnsafeValues_ShouldFail()
    {
        // Arrange
        var zeroNonceSequence = ImmutableArray.Create<BigInteger>(0, 2, 3, 4, 5, 6);
        var duplicateNonceSequence = ImmutableArray.Create<BigInteger>(7, 7, 8, 9, 10, 11);

        // Act
        var zeroNonceResult = ControlledElectionHarness.ValidateNonceSequence(
            zeroNonceSequence,
            ControlledElectionHarness.DefaultSelectionCount);
        var duplicateNonceResult = ControlledElectionHarness.ValidateNonceSequence(
            duplicateNonceSequence,
            ControlledElectionHarness.DefaultSelectionCount);

        // Assert
        zeroNonceResult.IsValid.Should().BeFalse();
        duplicateNonceResult.IsValid.Should().BeFalse();
        zeroNonceResult.FailureCode.Should().Be("UNSAFE_NONCE");
        duplicateNonceResult.FailureCode.Should().Be("UNSAFE_NONCE");
    }
}
