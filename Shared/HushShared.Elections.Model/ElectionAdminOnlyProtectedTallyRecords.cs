namespace HushShared.Elections.Model;

public static class ElectionAdminOnlyProtectedTallyCustodyModes
{
    public const string AwsKmsPerElectionEnvelopeV1 = "aws_kms_per_election_envelope_v1";
    public const string LegacyStaticAwsKmsV1 = "aws_kms_static_envelope_v1";
    public const string WindowsDpapiCurrentUserV1 = "windows_dpapi_current_user_v1";
    public const string TransparentTestV1 = "transparent_test_v1";
    public const string NotRequired = "not_required";
}

public enum ElectionAdminOnlyProtectedTallyCustodyLifecycleState
{
    NotRequired = 0,
    ProviderUnavailable = 1,
    KeyCreated = 2,
    SealedScalarPersisted = 3,
    OpenReady = 4,
    OpenBound = 5,
    ScalarDestroyed = 6,
    KeyDisabled = 7,
    DeletionScheduled = 8,
    RetryRequired = 9,
    ExceptionRequired = 10,
    OrphanedKey = 11,
    LegacyStaticKms = 12,
}

public record ElectionAdminOnlyProtectedTallyCustodyMetadata(
    string? CustodyMode = null,
    string? CustodyProvider = null,
    string? CustodyProviderProfile = null,
    string? KmsKeyId = null,
    string? KmsKeyArn = null,
    string? KmsAlias = null,
    string? KmsRegion = null,
    string? KmsAccountBoundary = null,
    string? KmsTagSetHash = null,
    DateTime? KmsTagsVerifiedAt = null,
    string? EncryptionContextVersion = null,
    string? EncryptionContextHash = null,
    ElectionAdminOnlyProtectedTallyCustodyLifecycleState CustodyLifecycleState =
        ElectionAdminOnlyProtectedTallyCustodyLifecycleState.NotRequired,
    string? CustodyLastAction = null,
    string? CustodyLastErrorCode = null,
    string? CustodyLastErrorMessage = null,
    int CustodyRetryCount = 0,
    DateTime? CustodyNextRetryAt = null,
    DateTime? LastReconciledAt = null,
    string? CustodyExceptionId = null,
    DateTime? KmsKeyCreatedAt = null,
    DateTime? KmsKeyDisabledAt = null,
    DateTime? KmsDeletionScheduledAt = null,
    DateTime? KmsDeletionDate = null,
    int? DeletionWindowDays = null,
    string? CustodyActionServiceIdentity = null,
    string? PublicCustodyReferenceHash = null,
    string? SealedEnvelopeHash = null)
{
    public string? CustodyMode { get; init; } = NormalizeOptionalText(CustodyMode);
    public string? CustodyProvider { get; init; } = NormalizeOptionalText(CustodyProvider);
    public string? CustodyProviderProfile { get; init; } = NormalizeOptionalText(CustodyProviderProfile);
    public string? KmsKeyId { get; init; } = NormalizeOptionalText(KmsKeyId);
    public string? KmsKeyArn { get; init; } = NormalizeOptionalText(KmsKeyArn);
    public string? KmsAlias { get; init; } = NormalizeOptionalText(KmsAlias);
    public string? KmsRegion { get; init; } = NormalizeOptionalText(KmsRegion);
    public string? KmsAccountBoundary { get; init; } = NormalizeOptionalText(KmsAccountBoundary);
    public string? KmsTagSetHash { get; init; } = NormalizeOptionalText(KmsTagSetHash);
    public string? EncryptionContextVersion { get; init; } = NormalizeOptionalText(EncryptionContextVersion);
    public string? EncryptionContextHash { get; init; } = NormalizeOptionalText(EncryptionContextHash);
    public string? CustodyLastAction { get; init; } = NormalizeOptionalText(CustodyLastAction);
    public string? CustodyLastErrorCode { get; init; } = NormalizeOptionalText(CustodyLastErrorCode);
    public string? CustodyLastErrorMessage { get; init; } = NormalizeOptionalText(CustodyLastErrorMessage);
    public int CustodyRetryCount { get; init; } = Math.Max(0, CustodyRetryCount);
    public string? CustodyExceptionId { get; init; } = NormalizeOptionalText(CustodyExceptionId);
    public int? DeletionWindowDays { get; init; } = DeletionWindowDays is > 0 ? DeletionWindowDays : null;
    public string? CustodyActionServiceIdentity { get; init; } =
        NormalizeOptionalText(CustodyActionServiceIdentity);
    public string? PublicCustodyReferenceHash { get; init; } = NormalizeOptionalText(PublicCustodyReferenceHash);
    public string? SealedEnvelopeHash { get; init; } = NormalizeOptionalText(SealedEnvelopeHash);

    internal static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

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
    DateTime LastUpdatedAt,
    string? CustodyMode = null,
    string? CustodyProvider = null,
    string? CustodyProviderProfile = null,
    string? KmsKeyId = null,
    string? KmsKeyArn = null,
    string? KmsAlias = null,
    string? KmsRegion = null,
    string? KmsAccountBoundary = null,
    string? KmsTagSetHash = null,
    DateTime? KmsTagsVerifiedAt = null,
    string? EncryptionContextVersion = null,
    string? EncryptionContextHash = null,
    ElectionAdminOnlyProtectedTallyCustodyLifecycleState CustodyLifecycleState =
        ElectionAdminOnlyProtectedTallyCustodyLifecycleState.NotRequired,
    string? CustodyLastAction = null,
    string? CustodyLastErrorCode = null,
    string? CustodyLastErrorMessage = null,
    int CustodyRetryCount = 0,
    DateTime? CustodyNextRetryAt = null,
    DateTime? LastReconciledAt = null,
    string? CustodyExceptionId = null,
    DateTime? KmsKeyCreatedAt = null,
    DateTime? KmsKeyDisabledAt = null,
    DateTime? KmsDeletionScheduledAt = null,
    DateTime? KmsDeletionDate = null,
    int? DeletionWindowDays = null,
    string? CustodyActionServiceIdentity = null,
    string? PublicCustodyReferenceHash = null,
    string? SealedEnvelopeHash = null)
{
    private const string StaticAwsKmsSealAlgorithm = "aws-kms-v1";

    public string? CustodyMode { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyMetadata.NormalizeOptionalText(CustodyMode);

    public string? CustodyProvider { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyMetadata.NormalizeOptionalText(CustodyProvider);

    public string? CustodyProviderProfile { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyMetadata.NormalizeOptionalText(CustodyProviderProfile);

    public string? KmsKeyId { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyMetadata.NormalizeOptionalText(KmsKeyId);

    public string? KmsKeyArn { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyMetadata.NormalizeOptionalText(KmsKeyArn);

    public string? KmsAlias { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyMetadata.NormalizeOptionalText(KmsAlias);

    public string? KmsRegion { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyMetadata.NormalizeOptionalText(KmsRegion);

    public string? KmsAccountBoundary { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyMetadata.NormalizeOptionalText(KmsAccountBoundary);

    public string? KmsTagSetHash { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyMetadata.NormalizeOptionalText(KmsTagSetHash);

    public string? EncryptionContextVersion { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyMetadata.NormalizeOptionalText(EncryptionContextVersion);

    public string? EncryptionContextHash { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyMetadata.NormalizeOptionalText(EncryptionContextHash);

    public string? CustodyLastAction { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyMetadata.NormalizeOptionalText(CustodyLastAction);

    public string? CustodyLastErrorCode { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyMetadata.NormalizeOptionalText(CustodyLastErrorCode);

    public string? CustodyLastErrorMessage { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyMetadata.NormalizeOptionalText(CustodyLastErrorMessage);

    public int CustodyRetryCount { get; init; } = Math.Max(0, CustodyRetryCount);

    public string? CustodyExceptionId { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyMetadata.NormalizeOptionalText(CustodyExceptionId);

    public int? DeletionWindowDays { get; init; } = DeletionWindowDays is > 0 ? DeletionWindowDays : null;

    public string? CustodyActionServiceIdentity { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyMetadata.NormalizeOptionalText(CustodyActionServiceIdentity);

    public string? PublicCustodyReferenceHash { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyMetadata.NormalizeOptionalText(PublicCustodyReferenceHash);

    public string? SealedEnvelopeHash { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyMetadata.NormalizeOptionalText(SealedEnvelopeHash);

    public bool HasPerElectionKmsCustody =>
        string.Equals(
            CustodyMode,
            ElectionAdminOnlyProtectedTallyCustodyModes.AwsKmsPerElectionEnvelopeV1,
            StringComparison.Ordinal);

    public ElectionAdminOnlyProtectedTallyCustodyLifecycleState ResolveCustodyLifecycleState()
    {
        if (CustodyLifecycleState != ElectionAdminOnlyProtectedTallyCustodyLifecycleState.NotRequired)
        {
            return CustodyLifecycleState;
        }

        if (string.IsNullOrWhiteSpace(CustodyMode) &&
            string.Equals(SealAlgorithm, StaticAwsKmsSealAlgorithm, StringComparison.Ordinal))
        {
            return ElectionAdminOnlyProtectedTallyCustodyLifecycleState.LegacyStaticKms;
        }

        if (DestroyedAt is not null)
        {
            return ElectionAdminOnlyProtectedTallyCustodyLifecycleState.ScalarDestroyed;
        }

        return ElectionAdminOnlyProtectedTallyCustodyLifecycleState.NotRequired;
    }
}
