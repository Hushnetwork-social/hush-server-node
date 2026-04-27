using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using HushNode.Credentials;
using HushNode.Reactions.Crypto;
using HushShared.Elections.Model;
using ReactionECPoint = HushShared.Reactions.Model.ECPoint;

namespace HushNode.Elections;

public static class ElectionProtectedTallyBinding
{
    public const string AdminOnlyProtectedCustodyProfileId = "admin-only-protected-custody-v1";
    private const string AdminOnlyProtectedCustodySeedScope = "admin-only-protected-custody-v2";

    public static ElectionCeremonyBindingSnapshot? ResolveBoundaryBinding(
        ElectionRecord election,
        ElectionBoundaryArtifactRecord? boundaryArtifact)
    {
        if (boundaryArtifact?.CeremonySnapshot is not null)
        {
            return boundaryArtifact.CeremonySnapshot;
        }

        return election.GovernanceMode == ElectionGovernanceMode.AdminOnly
            ? BuildAdminOnlyProtectedTallyBindingSnapshot(election)
            : null;
    }

    public static ElectionCeremonyBindingSnapshot? ResolveOpenBoundaryBinding(
        ElectionRecord election,
        ElectionBoundaryArtifactRecord? openArtifact) =>
        ResolveBoundaryBinding(election, openArtifact);

    public static ElectionCeremonyBindingSnapshot BuildAdminOnlyProtectedTallyBindingSnapshot(
        ElectionRecord election)
    {
        var bindingSeed =
            $"{election.ProtocolOmegaVersion}:admin-only-protected-custody:{election.ElectionId}:{election.CurrentDraftRevision}:{election.OwnerPublicAddress}";
        var projectedProfileId = ElectionSelectableProfileCatalog.NormalizeProfileId(
            ElectionGovernanceMode.AdminOnly,
            election.SelectedProfileId);

        return ElectionModelFactory.CreateCeremonyBindingSnapshot(
            CreateDeterministicGuid($"{bindingSeed}:binding"),
            ceremonyVersionNumber: 1,
            profileId: string.IsNullOrWhiteSpace(projectedProfileId)
                ? AdminOnlyProtectedCustodyProfileId
                : projectedProfileId,
            boundTrusteeCount: 1,
            requiredApprovalCount: 1,
            activeTrustees:
            [
                new ElectionTrusteeReference(election.OwnerPublicAddress, null),
            ],
            tallyPublicKeyFingerprint: ComputeScopedHash($"{bindingSeed}:tally-public-key"));
    }

    public static bool TryBuildAdminOnlyProtectedTallyBindingSnapshot(
        ElectionRecord election,
        ICredentialsProvider credentialsProvider,
        IBabyJubJub curve,
        out ElectionCeremonyBindingSnapshot? snapshot,
        out string error)
    {
        snapshot = null;
        if (!TryDeriveAdminOnlyProtectedTallyScalar(
                election,
                credentialsProvider,
                curve,
                out var scalar,
                out error))
        {
            return false;
        }

        try
        {
            var publicKey = curve.ScalarMul(curve.Generator, scalar);
            if (!curve.IsOnCurve(publicKey))
            {
                error = "Derived admin-only tally public key is not a valid Baby JubJub point.";
                return false;
            }

            var publicKeyBytes = publicKey.ToBytes();
            snapshot = ElectionModelFactory.CreateCeremonyBindingSnapshot(
                CreateDeterministicGuid($"{BuildBindingSeed(election)}:binding"),
                ceremonyVersionNumber: 1,
                profileId: ElectionSelectableProfileCatalog.NormalizeProfileId(
                    ElectionGovernanceMode.AdminOnly,
                    election.SelectedProfileId),
                boundTrusteeCount: 1,
                requiredApprovalCount: 1,
                activeTrustees:
                [
                    new ElectionTrusteeReference(election.OwnerPublicAddress, null),
                ],
                tallyPublicKeyFingerprint: ElectionTallyPublicKeyDerivation.ComputeFingerprint(publicKeyBytes),
                tallyPublicKey: publicKeyBytes);
            error = string.Empty;
            return true;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryBuildAdminOnlyProtectedTallyBindingSnapshot(
        ElectionRecord election,
        ElectionAdminOnlyProtectedTallyEnvelopeRecord envelope,
        IBabyJubJub curve,
        out ElectionCeremonyBindingSnapshot? snapshot,
        out string error)
    {
        snapshot = null;
        error = string.Empty;

        if (election.GovernanceMode != ElectionGovernanceMode.AdminOnly)
        {
            error = "Admin-only protected tally custody is only available for admin-only elections.";
            return false;
        }

        if (envelope.TallyPublicKey is not { Length: > 0 })
        {
            error = "The admin-only protected tally envelope is missing the tally public key.";
            return false;
        }

        if (!string.Equals(
                envelope.SelectedProfileId,
                ElectionSelectableProfileCatalog.NormalizeProfileId(
                    ElectionGovernanceMode.AdminOnly,
                    election.SelectedProfileId),
                StringComparison.Ordinal))
        {
            error = "The admin-only protected tally envelope does not match the election's selected profile.";
            return false;
        }

        try
        {
            var publicKey = ReactionECPoint.FromBytes(envelope.TallyPublicKey);
            if (!curve.IsOnCurve(publicKey))
            {
                error = "Stored admin-only tally public key is not a valid Baby JubJub point.";
                return false;
            }

            var fingerprint = ElectionTallyPublicKeyDerivation.ComputeFingerprint(envelope.TallyPublicKey);
            if (!string.Equals(
                    fingerprint,
                    envelope.TallyPublicKeyFingerprint,
                    StringComparison.Ordinal))
            {
                error = "Stored admin-only tally public key fingerprint does not match the public key bytes.";
                return false;
            }

            snapshot = ElectionModelFactory.CreateCeremonyBindingSnapshot(
                CreateDeterministicGuid($"{BuildBindingSeed(election)}:binding"),
                ceremonyVersionNumber: 1,
                profileId: envelope.SelectedProfileId,
                boundTrusteeCount: 1,
                requiredApprovalCount: 1,
                activeTrustees:
                [
                    new ElectionTrusteeReference(election.OwnerPublicAddress, null),
                ],
                tallyPublicKeyFingerprint: envelope.TallyPublicKeyFingerprint,
                tallyPublicKey: envelope.TallyPublicKey);
            return true;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryCreateAdminOnlyProtectedTallyEnvelope(
        ElectionRecord election,
        IAdminOnlyProtectedTallyEnvelopeCrypto envelopeCrypto,
        IBabyJubJub curve,
        out ElectionAdminOnlyProtectedTallyEnvelopeRecord? envelope,
        out ElectionCeremonyBindingSnapshot? snapshot,
        out string error,
        DateTime? createdAt = null)
    {
        envelope = null;
        snapshot = null;

        if (!envelopeCrypto.IsAvailable(out error))
        {
            return false;
        }

        if (election.GovernanceMode != ElectionGovernanceMode.AdminOnly)
        {
            error = "Admin-only protected tally custody is only available for admin-only elections.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(election.SelectedProfileId))
        {
            error = "Admin-only protected tally custody requires a selected circuit/profile id.";
            return false;
        }

        try
        {
            var scalar = CreateRandomNonZeroScalar(curve.Order);
            var publicKey = curve.ScalarMul(curve.Generator, scalar);
            if (!curve.IsOnCurve(publicKey))
            {
                error = "Generated admin-only protected tally public key is not a valid Baby JubJub point.";
                return false;
            }

            var publicKeyBytes = publicKey.ToBytes();
            var fingerprint = ElectionTallyPublicKeyDerivation.ComputeFingerprint(publicKeyBytes);
            var scalarText = scalar.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var timestamp = createdAt ?? DateTime.UtcNow;
            envelope = ElectionModelFactory.CreateAdminOnlyProtectedTallyEnvelope(
                election.ElectionId,
                ElectionSelectableProfileCatalog.NormalizeProfileId(
                    ElectionGovernanceMode.AdminOnly,
                    election.SelectedProfileId),
                publicKeyBytes,
                fingerprint,
                envelopeCrypto.SealPrivateScalar(
                    scalarText,
                    election.ElectionId,
                    election.SelectedProfileId),
                AdminOnlyProtectedTallyEnvelopeCryptoConstants.ScalarEncoding,
                envelopeCrypto.SealAlgorithm,
                envelopeCrypto.SealedByServiceIdentity,
                timestamp);

            return TryBuildAdminOnlyProtectedTallyBindingSnapshot(
                election,
                envelope,
                curve,
                out snapshot,
                out error);
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryUnsealAdminOnlyProtectedTallyScalar(
        ElectionRecord election,
        ElectionAdminOnlyProtectedTallyEnvelopeRecord envelope,
        IAdminOnlyProtectedTallyEnvelopeCrypto envelopeCrypto,
        IBabyJubJub curve,
        out BigInteger scalar,
        out string error)
    {
        scalar = BigInteger.Zero;
        error = string.Empty;

        if (!TryBuildAdminOnlyProtectedTallyBindingSnapshot(
                election,
                envelope,
                curve,
                out _,
                out error))
        {
            return false;
        }

        if (!string.Equals(
                envelope.ScalarEncoding,
                AdminOnlyProtectedTallyEnvelopeCryptoConstants.ScalarEncoding,
                StringComparison.Ordinal))
        {
            error = $"Unsupported admin-only tally scalar encoding {envelope.ScalarEncoding}.";
            return false;
        }

        var scalarText = envelopeCrypto.TryUnsealPrivateScalar(envelope, out error);
        if (string.IsNullOrWhiteSpace(scalarText))
        {
            return false;
        }

        if (!BigInteger.TryParse(
                scalarText,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out scalar))
        {
            error = "Unsealed admin-only protected tally scalar is not a valid integer.";
            return false;
        }

        scalar %= curve.Order;
        scalar = scalar == BigInteger.Zero ? BigInteger.One : scalar;

        try
        {
            var derived = curve.ScalarMul(curve.Generator, scalar);
            var expected = ReactionECPoint.FromBytes(envelope.TallyPublicKey);
            if (!derived.Equals(expected))
            {
                error = "Unsealed admin-only protected tally scalar does not match the stored public key.";
                return false;
            }
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }

        return true;
    }

    public static bool TryDeriveAdminOnlyProtectedTallyScalar(
        ElectionRecord election,
        ICredentialsProvider credentialsProvider,
        IBabyJubJub curve,
        out BigInteger scalar,
        out string error)
    {
        scalar = BigInteger.Zero;
        error = string.Empty;

        if (election.GovernanceMode != ElectionGovernanceMode.AdminOnly)
        {
            error = "Admin-only protected tally custody is only available for admin-only elections.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(election.SelectedProfileId))
        {
            error = "Admin-only protected tally custody requires a selected circuit/profile id.";
            return false;
        }

        var privateEncryptKey = credentialsProvider.GetCredentials().PrivateEncryptKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(privateEncryptKey))
        {
            error = "Admin-only protected tally custody requires the node private encryption key.";
            return false;
        }

        var seed = BuildBindingSeed(election);
        var digest = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(privateEncryptKey),
            Encoding.UTF8.GetBytes(seed));
        var normalized = new BigInteger(digest, isUnsigned: true, isBigEndian: true) % curve.Order;
        scalar = normalized == BigInteger.Zero ? BigInteger.One : normalized;
        return true;
    }

    public static bool TryValidateAdminOnlyProtectedTallyPublicKey(
        ElectionRecord election,
        ICredentialsProvider credentialsProvider,
        IBabyJubJub curve,
        byte[] expectedPublicKey,
        out string error)
    {
        if (!TryDeriveAdminOnlyProtectedTallyScalar(
                election,
                credentialsProvider,
                curve,
                out var scalar,
                out error))
        {
            return false;
        }

        try
        {
            var derived = curve.ScalarMul(curve.Generator, scalar);
            var expected = ReactionECPoint.FromBytes(expectedPublicKey);
            if (!derived.Equals(expected))
            {
                error = "Admin-only protected tally public key does not match the selected profile binding.";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string ComputeScopedHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", nameof(value));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim())));
    }

    private static Guid CreateDeterministicGuid(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", nameof(value));
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return new Guid(hash.AsSpan(0, 16));
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

    private static string BuildBindingSeed(ElectionRecord election) =>
        string.Join(
            ':',
            election.ProtocolOmegaVersion.Trim(),
            AdminOnlyProtectedCustodySeedScope,
            election.ElectionId,
            election.OwnerPublicAddress.Trim(),
            election.SelectedProfileId.Trim(),
            election.BindingStatus.ToString());
}
