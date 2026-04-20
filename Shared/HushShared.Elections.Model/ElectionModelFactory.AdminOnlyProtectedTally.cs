namespace HushShared.Elections.Model;

public static partial class ElectionModelFactory
{
    public static ElectionAdminOnlyProtectedTallyEnvelopeRecord CreateAdminOnlyProtectedTallyEnvelope(
        ElectionId electionId,
        string selectedProfileId,
        byte[] tallyPublicKey,
        string tallyPublicKeyFingerprint,
        string sealedTallyPrivateScalar,
        string scalarEncoding,
        string sealAlgorithm,
        string? sealedByServiceIdentity = null,
        DateTime? createdAt = null)
    {
        if (string.IsNullOrWhiteSpace(selectedProfileId))
        {
            throw new ArgumentException("Selected circuit/profile id is required.", nameof(selectedProfileId));
        }

        if (tallyPublicKey is not { Length: > 0 })
        {
            throw new ArgumentException("Tally public key is required.", nameof(tallyPublicKey));
        }

        if (string.IsNullOrWhiteSpace(tallyPublicKeyFingerprint))
        {
            throw new ArgumentException("Tally public key fingerprint is required.", nameof(tallyPublicKeyFingerprint));
        }

        if (string.IsNullOrWhiteSpace(sealedTallyPrivateScalar))
        {
            throw new ArgumentException("A sealed admin-only tally scalar is required.", nameof(sealedTallyPrivateScalar));
        }

        if (string.IsNullOrWhiteSpace(scalarEncoding))
        {
            throw new ArgumentException("Scalar encoding is required.", nameof(scalarEncoding));
        }

        if (string.IsNullOrWhiteSpace(sealAlgorithm))
        {
            throw new ArgumentException("Seal algorithm is required.", nameof(sealAlgorithm));
        }

        var timestamp = createdAt ?? DateTime.UtcNow;
        return new ElectionAdminOnlyProtectedTallyEnvelopeRecord(
            electionId,
            selectedProfileId.Trim(),
            CloneBytes(tallyPublicKey)!,
            tallyPublicKeyFingerprint.Trim(),
            sealedTallyPrivateScalar.Trim(),
            scalarEncoding.Trim(),
            sealAlgorithm.Trim(),
            timestamp,
            DestroyedAt: null,
            SealedByServiceIdentity: NormalizeOptionalText(sealedByServiceIdentity),
            LastUpdatedAt: timestamp);
    }
}
