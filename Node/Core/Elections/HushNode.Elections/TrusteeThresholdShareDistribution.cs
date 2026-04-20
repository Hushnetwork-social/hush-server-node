using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HushNode.Reactions.Crypto;
using HushShared.Elections.Model;
using Olimpo;

namespace HushNode.Elections;

internal static class TrusteeThresholdShareDistribution
{
    public const string TrusteeVaultMessageType = "trustee-share-vault-package";
    public const string ServerIssuedPayloadVersion = "omega-trustee-release-share-v1";
    public const string ServerIssuedMaterialKind = "server-issued-release-share";
    public const string ServerIssuedPackageKind = "server-issued-release-share";
    public const string CloseCountingShareFormat = "omega-controlled-threshold-scalar-v1";
    private const string CloseCountingSessionPurpose = "close-counting";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static TrusteeThresholdShareDistributionResult Create(
        ElectionRecord election,
        ElectionCeremonyVersionRecord version,
        IReadOnlyList<ElectionCeremonyTrusteeStateRecord> trusteeStates,
        IBabyJubJub curve)
    {
        ArgumentNullException.ThrowIfNull(election);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(trusteeStates);
        ArgumentNullException.ThrowIfNull(curve);

        if (version.RequiredApprovalCount < 1 || version.RequiredApprovalCount > version.TrusteeCount)
        {
            throw new InvalidOperationException("Ceremony threshold must be between one and the trustee count.");
        }

        if (version.BoundTrustees.Count != version.TrusteeCount)
        {
            throw new InvalidOperationException("Bound trustee roster must match the ceremony trustee count.");
        }

        var coefficients = Enumerable.Range(0, version.RequiredApprovalCount)
            .Select(_ => CreateRandomNonZeroScalar(curve.Order))
            .ToArray();
        var tallyPublicKey = curve.ScalarMul(curve.Generator, coefficients[0]).ToBytes();
        var assignments = new List<TrusteeThresholdShareAssignment>(version.BoundTrustees.Count);

        foreach (var trusteeBinding in version.BoundTrustees.Select((trustee, index) => new
                 {
                     Trustee = trustee,
                     ShareIndex = index + 1,
                 }))
        {
            var trusteeState = trusteeStates.FirstOrDefault(x =>
                string.Equals(
                    x.TrusteeUserAddress,
                    trusteeBinding.Trustee.TrusteeUserAddress,
                    StringComparison.OrdinalIgnoreCase));
            if (trusteeState is null || trusteeState.State != ElectionTrusteeCeremonyState.CeremonyCompleted)
            {
                throw new InvalidOperationException(
                    $"Completed ceremony state is required for trustee {trusteeBinding.Trustee.TrusteeUserAddress}.");
            }

            if (string.IsNullOrWhiteSpace(trusteeState.ShareVersion))
            {
                throw new InvalidOperationException(
                    $"Share version is required for trustee {trusteeBinding.Trustee.TrusteeUserAddress}.");
            }

            var scalar = EvaluatePolynomial(coefficients, trusteeBinding.ShareIndex, curve.Order);
            var shareMaterial = scalar.ToString(CultureInfo.InvariantCulture);
            assignments.Add(new TrusteeThresholdShareAssignment(
                trusteeBinding.Trustee.TrusteeUserAddress,
                trusteeBinding.Trustee.TrusteeDisplayName,
                trusteeBinding.ShareIndex,
                trusteeState.ShareVersion.Trim(),
                shareMaterial,
                ComputeHashHex(shareMaterial),
                curve.ScalarMul(curve.Generator, scalar).ToBytes()));
        }

        return new TrusteeThresholdShareDistributionResult(
            tallyPublicKey,
            ComputeFingerprint(tallyPublicKey),
            assignments);
    }

    public static string CreateEncryptedReleaseEnvelope(
        ElectionRecord election,
        ElectionCeremonyVersionRecord version,
        TrusteeThresholdShareAssignment assignment,
        string recipientPublicEncryptAddress)
    {
        ArgumentNullException.ThrowIfNull(election);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(assignment);

        if (string.IsNullOrWhiteSpace(recipientPublicEncryptAddress))
        {
            throw new ArgumentException("Recipient public encryption address is required.", nameof(recipientPublicEncryptAddress));
        }

        var payload = new TrusteeServerIssuedReleaseEnvelope(
            PackageVersion: ServerIssuedPayloadVersion,
            MaterialKind: ServerIssuedMaterialKind,
            ElectionId: election.ElectionId.ToString(),
            CeremonyVersionId: version.Id.ToString(),
            TrusteeUserAddress: assignment.TrusteeUserAddress,
            ShareVersion: assignment.ShareVersion,
            Material: new TrusteeServerIssuedReleaseMaterial(
                PackageKind: ServerIssuedPackageKind,
                SessionPurpose: CloseCountingSessionPurpose,
                ProtocolVersion: election.ProtocolOmegaVersion,
                ProfileId: version.ProfileId,
                VersionNumber: version.VersionNumber,
                CloseCountingShare: new TrusteeServerIssuedCloseCountingShare(
                    Format: CloseCountingShareFormat,
                    ScalarMaterial: assignment.ShareMaterial,
                    ScalarMaterialHash: assignment.ShareMaterialHash)));

        var serializedPayload = JsonSerializer.Serialize(payload, JsonOptions);
        return EncryptKeys.Encrypt(serializedPayload, recipientPublicEncryptAddress.Trim());
    }

    public static string ComputePayloadFingerprint(string encryptedPayload) =>
        ComputeHashHex(encryptedPayload ?? string.Empty);

    private static BigInteger EvaluatePolynomial(
        IReadOnlyList<BigInteger> coefficients,
        int x,
        BigInteger modulus)
    {
        var result = BigInteger.Zero;
        var power = BigInteger.One;

        foreach (var coefficient in coefficients)
        {
            result = Mod(result + (coefficient * power), modulus);
            power = Mod(power * x, modulus);
        }

        return result;
    }

    private static BigInteger CreateRandomNonZeroScalar(BigInteger order)
    {
        if (order <= BigInteger.One)
        {
            throw new ArgumentOutOfRangeException(nameof(order), "Curve order must be greater than one.");
        }

        var bufferSize = order.ToByteArray(isUnsigned: true, isBigEndian: true).Length;
        var buffer = new byte[bufferSize];

        while (true)
        {
            RandomNumberGenerator.Fill(buffer);
            var candidate = new BigInteger(buffer, isUnsigned: true, isBigEndian: true) % order;
            if (candidate != BigInteger.Zero)
            {
                return candidate;
            }
        }
    }

    private static BigInteger Mod(BigInteger value, BigInteger modulus)
    {
        var normalized = value % modulus;
        return normalized < 0 ? normalized + modulus : normalized;
    }

    private static string ComputeFingerprint(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value));

    private static string ComputeHashHex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

internal sealed record TrusteeThresholdShareDistributionResult(
    byte[] TallyPublicKey,
    string TallyPublicKeyFingerprint,
    IReadOnlyList<TrusteeThresholdShareAssignment> Assignments);

internal sealed record TrusteeThresholdShareAssignment(
    string TrusteeUserAddress,
    string? TrusteeDisplayName,
    int ShareIndex,
    string ShareVersion,
    string ShareMaterial,
    string ShareMaterialHash,
    byte[] CloseCountingPublicCommitment);

internal sealed record TrusteeServerIssuedReleaseEnvelope(
    string PackageVersion,
    string MaterialKind,
    string ElectionId,
    string CeremonyVersionId,
    string TrusteeUserAddress,
    string ShareVersion,
    TrusteeServerIssuedReleaseMaterial Material);

internal sealed record TrusteeServerIssuedReleaseMaterial(
    string PackageKind,
    string SessionPurpose,
    string ProtocolVersion,
    string ProfileId,
    int VersionNumber,
    TrusteeServerIssuedCloseCountingShare CloseCountingShare);

internal sealed record TrusteeServerIssuedCloseCountingShare(
    string Format,
    string ScalarMaterial,
    string ScalarMaterialHash);
