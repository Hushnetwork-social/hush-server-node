using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HushNode.Reactions.Crypto;
using HushShared.Reactions.Model;
using ReactionECPoint = HushShared.Reactions.Model.ECPoint;

namespace HushNode.Elections;

public sealed class ElectionBallotPublicationCryptoService(
    IBabyJubJub curve) : IElectionBallotPublicationCryptoService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IBabyJubJub _curve = curve;

    public ElectionBallotPublicationPreparationResult PrepareForPublication(
        string encryptedBallotPackage,
        string proofBundle)
    {
        try
        {
            var parsedBallot = ParseBallotPackage(encryptedBallotPackage);
            var rerandomizedSlots = new PublishedElectionCipherPointPair[parsedBallot.Ciphertext.C1.Length];

            for (var index = 0; index < rerandomizedSlots.Length; index++)
            {
                var nonce = CreateRandomNonce();
                var zeroCipher = EncryptZero(parsedBallot.PublicKey, nonce);
                var c1 = _curve.Add(parsedBallot.Ciphertext.C1[index], zeroCipher.C1);
                var c2 = _curve.Add(parsedBallot.Ciphertext.C2[index], zeroCipher.C2);

                rerandomizedSlots[index] = new PublishedElectionCipherPointPair(
                    ToPayload(c1),
                    ToPayload(c2));
            }

            var publishedPackage = new PublishedElectionBallotPackage(
                Version: parsedBallot.Version,
                PublicKey: ToPayload(parsedBallot.PublicKey),
                SelectionCount: parsedBallot.SelectionCount,
                Ciphertext: new PublishedElectionCiphertext(
                    rerandomizedSlots.Select(x => x.C1).ToArray(),
                    rerandomizedSlots.Select(x => x.C2).ToArray()));
            var serializedPackage = JsonSerializer.Serialize(publishedPackage, JsonOptions);
            var publishedProofBundle = JsonSerializer.Serialize(
                new PublishedElectionProofBundle(
                    Version: "ballot-publication-proof-v1",
                    Mode: "rerandomized",
                    SourceProofBundleHash: ComputeHexSha256(proofBundle),
                    PublishedBallotHash: ComputeHexSha256(serializedPackage)),
                JsonOptions);

            return ElectionBallotPublicationPreparationResult.Success(
                serializedPackage,
                publishedProofBundle);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            return ElectionBallotPublicationPreparationResult.Failure(
                "UNSUPPORTED_BALLOT_PAYLOAD",
                ex.Message);
        }
    }

    public ElectionBallotReplayResult ReplayPublishedBallots(IReadOnlyList<string> encryptedBallotPackages)
    {
        if (encryptedBallotPackages is null || encryptedBallotPackages.Count == 0)
        {
            return ElectionBallotReplayResult.Success(SHA256.HashData(Array.Empty<byte>()));
        }

        try
        {
            var parsedPackages = encryptedBallotPackages
                .Select(ParseBallotPackage)
                .ToArray();
            var first = parsedPackages[0];
            var tallyC1 = Enumerable.Repeat(_curve.Identity, first.SelectionCount).ToArray();
            var tallyC2 = Enumerable.Repeat(_curve.Identity, first.SelectionCount).ToArray();

            foreach (var package in parsedPackages)
            {
                if (package.SelectionCount != first.SelectionCount)
                {
                    throw new InvalidOperationException("Published ballots must use the same selection count.");
                }

                if (!PointEquals(package.PublicKey, first.PublicKey))
                {
                    throw new InvalidOperationException("Published ballots must use the same election public key.");
                }

                for (var index = 0; index < package.SelectionCount; index++)
                {
                    tallyC1[index] = _curve.Add(tallyC1[index], package.Ciphertext.C1[index]);
                    tallyC2[index] = _curve.Add(tallyC2[index], package.Ciphertext.C2[index]);
                }
            }

            var serializedTally = JsonSerializer.Serialize(
                new PublishedElectionCiphertext(
                    tallyC1.Select(ToPayload).ToArray(),
                    tallyC2.Select(ToPayload).ToArray()),
                JsonOptions);

            return ElectionBallotReplayResult.Success(
                SHA256.HashData(Encoding.UTF8.GetBytes(serializedTally)));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            return ElectionBallotReplayResult.Failure(
                "UNSUPPORTED_BALLOT_PAYLOAD",
                ex.Message);
        }
    }

    private ParsedElectionBallotPackage ParseBallotPackage(string encryptedBallotPackage)
    {
        if (string.IsNullOrWhiteSpace(encryptedBallotPackage))
        {
            throw new ArgumentException("Encrypted ballot package is required.", nameof(encryptedBallotPackage));
        }

        var payload = JsonSerializer.Deserialize<PublishedElectionBallotPackage>(encryptedBallotPackage, JsonOptions)
            ?? throw new InvalidOperationException("Encrypted ballot package could not be deserialized.");

        if (payload.SelectionCount <= 0)
        {
            throw new InvalidOperationException("Election ballot package selection count must be positive.");
        }

        if (payload.Ciphertext?.C1 is null ||
            payload.Ciphertext.C2 is null ||
            payload.Ciphertext.C1.Length != payload.SelectionCount ||
            payload.Ciphertext.C2.Length != payload.SelectionCount)
        {
            throw new InvalidOperationException("Election ballot package ciphertext dimensions are invalid.");
        }

        return new ParsedElectionBallotPackage(
            payload.Version ?? "election-ballot.v1",
            ParsePoint(payload.PublicKey, "publicKey"),
            payload.SelectionCount,
            new ParsedElectionCiphertext(
                payload.Ciphertext.C1.Select((point, index) => ParsePoint(point, $"ciphertext.c1[{index}]")).ToArray(),
                payload.Ciphertext.C2.Select((point, index) => ParsePoint(point, $"ciphertext.c2[{index}]")).ToArray()));
    }

    private ControlledElectionCipherPointPair EncryptZero(ReactionECPoint publicKey, BigInteger nonce)
    {
        var c1 = _curve.ScalarMul(_curve.Generator, nonce);
        var c2 = _curve.ScalarMul(publicKey, nonce);
        return new ControlledElectionCipherPointPair(c1, c2);
    }

    private BigInteger CreateRandomNonce()
    {
        Span<byte> buffer = stackalloc byte[64];
        RandomNumberGenerator.Fill(buffer);
        var scalar = new BigInteger(buffer, isUnsigned: true, isBigEndian: true) % _curve.Order;
        return scalar == BigInteger.Zero ? BigInteger.One : scalar;
    }

    private static PublishedElectionPointPayload ToPayload(ReactionECPoint point) =>
        new(
            point.X.ToString(CultureInfo.InvariantCulture),
            point.Y.ToString(CultureInfo.InvariantCulture));

    private static ReactionECPoint ParsePoint(PublishedElectionPointPayload? point, string label)
    {
        if (point is null ||
            string.IsNullOrWhiteSpace(point.X) ||
            string.IsNullOrWhiteSpace(point.Y))
        {
            throw new InvalidOperationException($"Election ballot point '{label}' is required.");
        }

        return new ReactionECPoint(
            BigInteger.Parse(point.X, CultureInfo.InvariantCulture),
            BigInteger.Parse(point.Y, CultureInfo.InvariantCulture));
    }

    private static string ComputeHexSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));

    private static bool PointEquals(ReactionECPoint left, ReactionECPoint right) =>
        left.X == right.X && left.Y == right.Y;

    private sealed record PublishedElectionBallotPackage(
        string? Version,
        PublishedElectionPointPayload? PublicKey,
        int SelectionCount,
        PublishedElectionCiphertext? Ciphertext);

    private sealed record PublishedElectionCiphertext(
        PublishedElectionPointPayload[] C1,
        PublishedElectionPointPayload[] C2);

    private sealed record PublishedElectionPointPayload(
        string X,
        string Y);

    private sealed record PublishedElectionProofBundle(
        string Version,
        string Mode,
        string SourceProofBundleHash,
        string PublishedBallotHash);

    private sealed record ParsedElectionBallotPackage(
        string Version,
        ReactionECPoint PublicKey,
        int SelectionCount,
        ParsedElectionCiphertext Ciphertext);

    private sealed record ParsedElectionCiphertext(
        ReactionECPoint[] C1,
        ReactionECPoint[] C2);

    private sealed record PublishedElectionCipherPointPair(
        PublishedElectionPointPayload C1,
        PublishedElectionPointPayload C2);

    private sealed record ControlledElectionCipherPointPair(
        ReactionECPoint C1,
        ReactionECPoint C2);
}
