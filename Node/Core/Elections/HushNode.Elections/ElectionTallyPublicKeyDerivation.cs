using System.Numerics;
using System.Security.Cryptography;
using HushNode.Reactions.Crypto;
using HushShared.Elections.Model;
using HushShared.Reactions.Model;
using ReactionECPoint = HushShared.Reactions.Model.ECPoint;

namespace HushNode.Elections;

internal static class ElectionTallyPublicKeyDerivation
{
    public static bool TryParsePointPayload(
        ElectionCurvePointPayload? payload,
        IBabyJubJub curve,
        out byte[]? pointBytes,
        out string error)
    {
        pointBytes = null;
        error = string.Empty;

        if (payload is null ||
            string.IsNullOrWhiteSpace(payload.X) ||
            string.IsNullOrWhiteSpace(payload.Y))
        {
            error = "Close-counting public commitment is required.";
            return false;
        }

        try
        {
            var xBytes = Convert.FromBase64String(payload.X.Trim());
            var yBytes = Convert.FromBase64String(payload.Y.Trim());
            if (xBytes.Length != 32 || yBytes.Length != 32)
            {
                error = "Close-counting public commitment coordinates must each be 32 bytes.";
                return false;
            }

            var point = ReactionECPoint.FromCoordinates(xBytes, yBytes);
            if (!curve.IsOnCurve(point))
            {
                error = "Close-counting public commitment must be a valid Baby JubJub point.";
                return false;
            }

            pointBytes = point.ToBytes();
            return true;
        }
        catch (FormatException)
        {
            error = "Close-counting public commitment must use base64 coordinates.";
            return false;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static ReactionECPoint DeserializePoint(byte[] pointBytes) => ReactionECPoint.FromBytes(pointBytes);

    public static string ComputeFingerprint(byte[] pointBytes) =>
        Convert.ToHexString(SHA256.HashData(pointBytes));

    public static bool TryDeriveThresholdPublicKey(
        ElectionCeremonyVersionRecord version,
        IReadOnlyList<ElectionCeremonyTrusteeStateRecord> trusteeStates,
        IBabyJubJub curve,
        out byte[]? publicKeyBytes,
        out string error)
    {
        publicKeyBytes = null;
        error = string.Empty;

        if (version.RequiredApprovalCount < 1)
        {
            error = "Ceremony versions must require at least one trustee approval.";
            return false;
        }

        var selectedCommitments = version.BoundTrustees
            .Select((boundTrustee, index) => new
            {
                boundTrustee.TrusteeUserAddress,
                ShareIndex = index + 1,
                State = trusteeStates.FirstOrDefault(x =>
                    string.Equals(
                        x.TrusteeUserAddress,
                        boundTrustee.TrusteeUserAddress,
                        StringComparison.OrdinalIgnoreCase)),
            })
            .Where(x =>
                x.State?.State == ElectionTrusteeCeremonyState.CeremonyCompleted &&
                x.State.CloseCountingPublicCommitment is { Length: > 0 })
            .Take(version.RequiredApprovalCount)
            .ToArray();

        if (selectedCommitments.Length < version.RequiredApprovalCount)
        {
            error = "Ready ceremony versions require close-counting public commitments for the threshold trustee set.";
            return false;
        }

        try
        {
            var aggregate = curve.Identity;
            foreach (var selected in selectedCommitments)
            {
                var commitmentPoint = DeserializePoint(selected.State!.CloseCountingPublicCommitment!);
                if (!curve.IsOnCurve(commitmentPoint))
                {
                    error = $"Stored close-counting public commitment for trustee {selected.TrusteeUserAddress} is not a valid Baby JubJub point.";
                    return false;
                }

                var numerator = BigInteger.One;
                var denominator = BigInteger.One;
                foreach (var other in selectedCommitments)
                {
                    if (other.ShareIndex == selected.ShareIndex)
                    {
                        continue;
                    }

                    var otherIndex = new BigInteger(other.ShareIndex);
                    numerator = Mod(numerator * (-otherIndex), curve.Order);
                    denominator = Mod(
                        denominator * (new BigInteger(selected.ShareIndex) - otherIndex),
                        curve.Order);
                }

                var coefficient = Mod(numerator * ModInverse(denominator, curve.Order), curve.Order);
                aggregate = curve.Add(aggregate, curve.ScalarMul(commitmentPoint, coefficient));
            }

            if (!curve.IsOnCurve(aggregate))
            {
                error = "Derived ceremony tally public key is not on the Baby JubJub curve.";
                return false;
            }

            publicKeyBytes = aggregate.ToBytes();
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
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
            throw new InvalidOperationException("Close-counting public commitment denominator is not invertible.");
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
            throw new InvalidOperationException("Close-counting public commitment denominator is not invertible.");
        }

        return t < 0 ? t + modulus : t;
    }
}
