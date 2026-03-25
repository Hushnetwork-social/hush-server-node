using System.Collections.Immutable;
using System.Numerics;
using FluentAssertions;
using HushServerNode.Testing.Elections;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class ControlledElectionRerandomizationTests
{
    [Fact]
    public void RerandomizeBallot_ShouldChangeCiphertextRepresentationWithoutChangingBallotMeaning()
    {
        // Arrange
        var keyPair = ControlledElectionHarness.CreateDeterministicKeyPair(1201);
        var originalBallot = ControlledElectionHarness.EncryptOneHotBallot(
            ballotId: "ballot-rerandomize-001",
            choiceIndex: 3,
            publicKey: keyPair.PublicKey,
            nonces: ControlledElectionHarness.CreateDeterministicNonceSequence(
                1301,
                ControlledElectionHarness.DefaultSelectionCount));

        // Act
        var rerandomizedBallot = ControlledElectionHarness.RerandomizeBallot(
            originalBallot,
            keyPair.PublicKey,
            ControlledElectionHarness.CreateDeterministicNonceSequence(
                2301,
                ControlledElectionHarness.DefaultSelectionCount));
        var originalMeaning = ControlledElectionHarness.TryDecryptBallotForHarness(
            originalBallot,
            keyPair.PrivateKey,
            maxSupportedCount: 1);
        var rerandomizedMeaning = ControlledElectionHarness.TryDecryptBallotForHarness(
            rerandomizedBallot,
            keyPair.PrivateKey,
            maxSupportedCount: 1);

        // Assert
        CreateBallotFingerprint(rerandomizedBallot).Should().NotBe(CreateBallotFingerprint(originalBallot));
        originalMeaning.IsSuccessful.Should().BeTrue();
        rerandomizedMeaning.IsSuccessful.Should().BeTrue();
        rerandomizedMeaning.DecodedCounts.Should().Equal(originalMeaning.DecodedCounts);
        rerandomizedMeaning.DecodedCounts.Should().Equal(new BigInteger[] { 0, 0, 0, 1, 0, 0 });
    }

    [Fact]
    public void RerandomizeBallotSet_ShouldPreserveAggregateTallySemantics()
    {
        // Arrange
        var keyPair = ControlledElectionHarness.CreateDeterministicKeyPair(3201);
        var originalBallots = ImmutableArray.Create(
            ControlledElectionHarness.EncryptOneHotBallot(
                ballotId: "ballot-1",
                choiceIndex: 0,
                publicKey: keyPair.PublicKey,
                nonces: ControlledElectionHarness.CreateDeterministicNonceSequence(4001, ControlledElectionHarness.DefaultSelectionCount)),
            ControlledElectionHarness.EncryptOneHotBallot(
                ballotId: "ballot-2",
                choiceIndex: 2,
                publicKey: keyPair.PublicKey,
                nonces: ControlledElectionHarness.CreateDeterministicNonceSequence(5001, ControlledElectionHarness.DefaultSelectionCount)),
            ControlledElectionHarness.EncryptOneHotBallot(
                ballotId: "ballot-3",
                choiceIndex: 2,
                publicKey: keyPair.PublicKey,
                nonces: ControlledElectionHarness.CreateDeterministicNonceSequence(6001, ControlledElectionHarness.DefaultSelectionCount)));
        var rerandomizedBallots = originalBallots
            .Select((ballot, index) => ControlledElectionHarness.RerandomizeBallot(
                ballot,
                keyPair.PublicKey,
                ControlledElectionHarness.CreateDeterministicNonceSequence(
                    7001 + (index * 100),
                    ControlledElectionHarness.DefaultSelectionCount)))
            .ToImmutableArray();

        // Act
        var originalTally = ControlledElectionHarness.AccumulateBallots("election-rerandomize", originalBallots);
        var rerandomizedTally = ControlledElectionHarness.AccumulateBallots("election-rerandomize", rerandomizedBallots);
        var originalMeaning = ControlledElectionHarness.TryDecryptTallyForHarness(
            originalTally,
            keyPair.PrivateKey,
            maxSupportedCount: 3);
        var rerandomizedMeaning = ControlledElectionHarness.TryDecryptTallyForHarness(
            rerandomizedTally,
            keyPair.PrivateKey,
            maxSupportedCount: 3);

        // Assert
        ControlledElectionHarness.CreateTallyFingerprint(rerandomizedTally)
            .Should().NotBe(ControlledElectionHarness.CreateTallyFingerprint(originalTally));
        originalMeaning.IsSuccessful.Should().BeTrue();
        rerandomizedMeaning.IsSuccessful.Should().BeTrue();
        rerandomizedMeaning.DecodedCounts.Should().Equal(originalMeaning.DecodedCounts);
        rerandomizedMeaning.DecodedCounts.Should().Equal(new BigInteger[] { 1, 0, 2, 0, 0, 0 });
    }

    [Fact]
    public void RerandomizeBallot_WithUnsafeNonceInput_ShouldReject()
    {
        // Arrange
        var keyPair = ControlledElectionHarness.CreateDeterministicKeyPair(8101);
        var ballot = ControlledElectionHarness.EncryptOneHotBallot(
            ballotId: "ballot-rerandomize-unsafe",
            choiceIndex: 1,
            publicKey: keyPair.PublicKey,
            nonces: ControlledElectionHarness.CreateDeterministicNonceSequence(
                8201,
                ControlledElectionHarness.DefaultSelectionCount));

        // Act
        var rerandomize = () => ControlledElectionHarness.RerandomizeBallot(
            ballot,
            keyPair.PublicKey,
            ImmutableArray.Create(91, 92, 93, 94, 95, 95).Select(value => new BigInteger(value)).ToImmutableArray());

        // Assert
        rerandomize.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*duplicate values*");
    }

    private static string CreateBallotFingerprint(ControlledElectionBallot ballot) =>
        string.Join(
            "|",
            ballot.Slots.Select(slot =>
                $"{slot.C1.X}:{slot.C1.Y}:{slot.C2.X}:{slot.C2.Y}"));
}
