namespace HushShared.Elections.Model;

public record ElectionAdminOnlyProtectedTallyEnvelopeRecord(
    ElectionId ElectionId,
    string SelectedProfileId,
    byte[] TallyPublicKey,
    string TallyPublicKeyFingerprint,
    string SealedTallyPrivateScalar,
    string ScalarEncoding,
    string SealAlgorithm,
    DateTime CreatedAt,
    DateTime? DestroyedAt,
    string? SealedByServiceIdentity,
    DateTime LastUpdatedAt);
