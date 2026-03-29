using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HushNode.Credentials;
using HushNode.Reactions.Crypto;
using HushShared.Elections.Model;
using HushShared.Reactions.Model;
using Olimpo;
using ECPoint = HushShared.Reactions.Model.ECPoint;

namespace HushNode.Elections;

public sealed class ElectionResultCryptoService(
    IBabyJubJub curve,
    ICredentialsProvider credentialsProvider) : IElectionResultCryptoService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IBabyJubJub _curve = curve;
    private readonly ICredentialsProvider _credentialsProvider = credentialsProvider;

    public ElectionAggregateReleaseResult TryReleaseAggregateTally(
        IReadOnlyList<string> encryptedBallotPackages,
        IReadOnlyList<ElectionFinalizationShareRecord> acceptedShares,
        int maxSupportedCount)
    {
        if (maxSupportedCount < 0)
        {
            return ElectionAggregateReleaseResult.Failure(
                "INVALID_DECODE_BOUND",
                "Decode bound must be zero or greater.");
        }

        if (encryptedBallotPackages is null || encryptedBallotPackages.Count == 0)
        {
            return ElectionAggregateReleaseResult.Success(
                SHA256.HashData(Array.Empty<byte>()),
                Array.Empty<int>());
        }

        if (acceptedShares is null || acceptedShares.Count == 0)
        {
            return ElectionAggregateReleaseResult.Failure(
                "INSUFFICIENT_SHARES",
                "At least one accepted share is required to release the aggregate tally.");
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
            var finalEncryptedTallyHash = SHA256.HashData(Encoding.UTF8.GetBytes(serializedTally));

            var reconstructedSecret = ReconstructSecretScalarFromShares(acceptedShares);
            var derivedPublicKey = _curve.ScalarMul(_curve.Generator, reconstructedSecret);
            if (!PointEquals(derivedPublicKey, first.PublicKey))
            {
                return ElectionAggregateReleaseResult.Failure(
                    "MALFORMED_SHARE",
                    "Accepted shares reconstruct a different public key than the published ballots.");
            }

            var decodedCounts = new List<int>(first.SelectionCount);
            for (var index = 0; index < first.SelectionCount; index++)
            {
                var releasedSelection = _curve.Subtract(
                    tallyC2[index],
                    _curve.ScalarMul(tallyC1[index], reconstructedSecret));
                var decodedCount = TryDecodePointToCount(releasedSelection, maxSupportedCount);
                if (!decodedCount.HasValue)
                {
                    return ElectionAggregateReleaseResult.Failure(
                        "DECODE_BOUND_EXCEEDED",
                        $"Could not decode released tally slot within bound '{maxSupportedCount}'.");
                }

                decodedCounts.Add(decodedCount.Value);
            }

            return ElectionAggregateReleaseResult.Success(
                finalEncryptedTallyHash,
                decodedCounts);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            return ElectionAggregateReleaseResult.Failure(
                "UNSUPPORTED_BALLOT_PAYLOAD",
                ex.Message);
        }
    }

    public string EncryptForElectionParticipants(
        string plaintextPayload,
        string nodeEncryptedElectionPrivateKey)
    {
        if (string.IsNullOrWhiteSpace(plaintextPayload))
        {
            throw new ArgumentException("Plaintext payload is required.", nameof(plaintextPayload));
        }

        if (string.IsNullOrWhiteSpace(nodeEncryptedElectionPrivateKey))
        {
            throw new ArgumentException("Node-encrypted election private key is required.", nameof(nodeEncryptedElectionPrivateKey));
        }

        var nodePrivateEncryptKey = _credentialsProvider.GetCredentials().PrivateEncryptKey;
        var electionPrivateKey = EncryptKeys.Decrypt(nodeEncryptedElectionPrivateKey, nodePrivateEncryptKey);
        var electionPublicKey = EncryptKeys.DerivePublicKey(electionPrivateKey);
        return EncryptKeys.Encrypt(plaintextPayload, electionPublicKey);
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
            ParsePoint(payload.PublicKey, "publicKey"),
            payload.SelectionCount,
            new ParsedElectionCiphertext(
                payload.Ciphertext.C1.Select((point, index) => ParsePoint(point, $"ciphertext.c1[{index}]")).ToArray(),
                payload.Ciphertext.C2.Select((point, index) => ParsePoint(point, $"ciphertext.c2[{index}]")).ToArray()));
    }

    private BigInteger ReconstructSecretScalarFromShares(IReadOnlyList<ElectionFinalizationShareRecord> shares)
    {
        var duplicateTrustees = shares
            .GroupBy(share => share.TrusteeUserAddress, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateTrustees.Length > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate trustee shares cannot reconstruct the aggregate tally secret: {string.Join(", ", duplicateTrustees)}.");
        }

        var duplicateIndices = shares
            .GroupBy(share => share.ShareIndex)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateIndices.Length > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate share indexes cannot reconstruct the aggregate tally secret: {string.Join(", ", duplicateIndices)}.");
        }

        var secret = BigInteger.Zero;
        foreach (var share in shares)
        {
            var xCoordinate = new BigInteger(share.ShareIndex);
            var yCoordinate = ParseScalar(share.ShareMaterial);
            var numerator = BigInteger.One;
            var denominator = BigInteger.One;

            foreach (var otherShare in shares)
            {
                if (ReferenceEquals(share, otherShare))
                {
                    continue;
                }

                var otherX = new BigInteger(otherShare.ShareIndex);
                numerator = Mod(numerator * (-otherX), _curve.Order);
                denominator = Mod(denominator * (xCoordinate - otherX), _curve.Order);
            }

            var lagrangeCoefficient = Mod(
                numerator * ModInverse(denominator, _curve.Order),
                _curve.Order);
            secret = Mod(secret + (yCoordinate * lagrangeCoefficient), _curve.Order);
        }

        return secret;
    }

    private int? TryDecodePointToCount(ECPoint releasedSelection, int maxSupportedCount)
    {
        var current = _curve.Identity;
        if (PointEquals(releasedSelection, current))
        {
            return 0;
        }

        for (var count = 1; count <= maxSupportedCount; count++)
        {
            current = _curve.Add(current, _curve.Generator);
            if (PointEquals(releasedSelection, current))
            {
                return count;
            }
        }

        return null;
    }

    private static BigInteger ParseScalar(string shareMaterial)
    {
        if (string.IsNullOrWhiteSpace(shareMaterial))
        {
            throw new InvalidOperationException("Share material is required.");
        }

        return BigInteger.Parse(shareMaterial, CultureInfo.InvariantCulture);
    }

    private static BigInteger Mod(BigInteger value, BigInteger modulus)
    {
        var normalized = value % modulus;
        return normalized < 0 ? normalized + modulus : normalized;
    }

    private static BigInteger ModInverse(BigInteger value, BigInteger modulus)
    {
        var normalizedValue = Mod(value, modulus);
        if (normalizedValue == BigInteger.Zero)
        {
            throw new InvalidOperationException("Cannot invert zero while reconstructing aggregate tally shares.");
        }

        var t = BigInteger.Zero;
        var newT = BigInteger.One;
        var r = modulus;
        var newR = normalizedValue;

        while (newR != BigInteger.Zero)
        {
            var quotient = r / newR;
            (t, newT) = (newT, t - (quotient * newT));
            (r, newR) = (newR, r - (quotient * newR));
        }

        if (r > BigInteger.One)
        {
            throw new InvalidOperationException("Aggregate tally share denominator is not invertible.");
        }

        return t < 0 ? t + modulus : t;
    }

    private static PublishedElectionPointPayload ToPayload(ECPoint point) =>
        new(
            point.X.ToString(CultureInfo.InvariantCulture),
            point.Y.ToString(CultureInfo.InvariantCulture));

    private static ECPoint ParsePoint(PublishedElectionPointPayload? point, string label)
    {
        if (point is null ||
            string.IsNullOrWhiteSpace(point.X) ||
            string.IsNullOrWhiteSpace(point.Y))
        {
            throw new InvalidOperationException($"Election ballot point '{label}' is required.");
        }

        return new ECPoint(
            BigInteger.Parse(point.X, CultureInfo.InvariantCulture),
            BigInteger.Parse(point.Y, CultureInfo.InvariantCulture));
    }

    private static bool PointEquals(ECPoint left, ECPoint right) =>
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

    private sealed record ParsedElectionBallotPackage(
        ECPoint PublicKey,
        int SelectionCount,
        ParsedElectionCiphertext Ciphertext);

    private sealed record ParsedElectionCiphertext(
        ECPoint[] C1,
        ECPoint[] C2);
}
