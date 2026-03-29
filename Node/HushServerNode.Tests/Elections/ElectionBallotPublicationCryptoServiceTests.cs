using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HushNode.Elections;
using HushNode.Reactions.Crypto;
using HushServerNode.Testing.Elections;
using ReactionECPoint = HushShared.Reactions.Model.ECPoint;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class ElectionBallotPublicationCryptoServiceTests
{
    [Fact]
    public void ReplayPublishedBallots_WhenPublicKeysMatch_ReturnsEncryptedTallyHash()
    {
        var service = new ElectionBallotPublicationCryptoService(new BabyJubJubCurve());
        var publicKeySeed = new BigInteger(9501);
        var packageA = BuildEncryptedBallotPackage(
            electionKeySeed: publicKeySeed,
            ballotSeed: 9601,
            choiceIndex: 0);
        var packageB = BuildEncryptedBallotPackage(
            electionKeySeed: publicKeySeed,
            ballotSeed: 9701,
            choiceIndex: 2);

        var result = service.ReplayPublishedBallots([packageA, packageB]);

        result.IsSuccessful.Should().BeTrue();
        result.FinalEncryptedTallyHash.Should().NotBeNull().And.NotBeEmpty();
    }

    [Fact]
    public void ReplayPublishedBallots_WhenPublicKeysDiffer_FailsValidation()
    {
        var service = new ElectionBallotPublicationCryptoService(new BabyJubJubCurve());
        var packageA = BuildEncryptedBallotPackage(
            electionKeySeed: new BigInteger(9501),
            ballotSeed: 9601,
            choiceIndex: 0);
        var packageB = BuildEncryptedBallotPackage(
            electionKeySeed: new BigInteger(9502),
            ballotSeed: 9701,
            choiceIndex: 2);

        var result = service.ReplayPublishedBallots([packageA, packageB]);

        result.IsSuccessful.Should().BeFalse();
        result.FailureCode.Should().Be("UNSUPPORTED_BALLOT_PAYLOAD");
        result.FailureReason.Should().Contain("same election public key");
    }

    private static string BuildEncryptedBallotPackage(
        BigInteger electionKeySeed,
        int ballotSeed,
        int choiceIndex)
    {
        var curve = new BabyJubJubCurve();
        var keyPair = ControlledElectionHarness.CreateDeterministicKeyPair(
            NormalizeSeed(electionKeySeed, curve.Order),
            curve);
        var ballot = ControlledElectionHarness.EncryptOneHotBallot(
            ballotId: $"feat100-crypto-test-{ballotSeed}",
            choiceIndex: choiceIndex,
            publicKey: keyPair.PublicKey,
            nonces: ControlledElectionHarness.CreateDeterministicNonceSequence(
                ballotSeed,
                ControlledElectionHarness.DefaultSelectionCount,
                curve),
            selectionCount: ControlledElectionHarness.DefaultSelectionCount,
            curve: curve);

        var payload = new PublishedElectionBallotPackage(
            Version: "election-ballot.v1",
            PublicKey: ToPoint(keyPair.PublicKey),
            SelectionCount: ControlledElectionHarness.DefaultSelectionCount,
            Ciphertext: new PublishedElectionCiphertext(
                ballot.Slots.Select(slot => ToPoint(slot.C1)).ToArray(),
                ballot.Slots.Select(slot => ToPoint(slot.C2)).ToArray()));

        return JsonSerializer.Serialize(payload);
    }

    private static BigInteger NormalizeSeed(BigInteger seed, BigInteger order)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(seed.ToString(CultureInfo.InvariantCulture)));
        var scalar = new BigInteger(digest, isUnsigned: true, isBigEndian: true) % order;
        return scalar == BigInteger.Zero ? BigInteger.One : scalar;
    }

    private static PublishedElectionPointPayload ToPoint(ReactionECPoint point) =>
        new(
            point.X.ToString(CultureInfo.InvariantCulture),
            point.Y.ToString(CultureInfo.InvariantCulture));

    private sealed record PublishedElectionBallotPackage(
        string Version,
        PublishedElectionPointPayload PublicKey,
        int SelectionCount,
        PublishedElectionCiphertext Ciphertext);

    private sealed record PublishedElectionCiphertext(
        PublishedElectionPointPayload[] C1,
        PublishedElectionPointPayload[] C2);

    private sealed record PublishedElectionPointPayload(
        string X,
        string Y);
}
