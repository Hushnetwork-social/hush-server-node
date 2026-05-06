namespace HushShared.Elections.Model;

public static class ElectionSp06ProfileIds
{
    public const string HighAssuranceIndependentTrusteesV1 = "high_assurance_independent_trustees_v1";
    public const string HighAssuranceIndependentTrusteesV1Version = "1.0.0";
    public const string TrusteeLocalSecureVaultV1 = "trustee_local_secure_vault_v1";
    public const string ManagedTrusteeAppV1 = "managed_trustee_app_v1";
    public const string KmsOrHsmTrusteeV1 = "kms_or_hsm_trustee_v1";
    public const string SharedOperatorCustodyV1 = "shared_operator_custody_v1";

    public static IReadOnlySet<string> HighAssuranceV1AllowedCustodyModes { get; } = new HashSet<string>(
        [
            TrusteeLocalSecureVaultV1,
            ManagedTrusteeAppV1,
        ],
        StringComparer.Ordinal);

    public static bool IsHighAssuranceV1AllowedCustodyMode(string? custodyMode) =>
        !string.IsNullOrWhiteSpace(custodyMode) &&
        HighAssuranceV1AllowedCustodyModes.Contains(custodyMode.Trim());
}

public enum ElectionTrusteeControlDomainEvidenceStatus
{
    Accepted = 0,
    Missing = 1,
    Stale = 2,
    Incompatible = 3,
    Rejected = 4,
}

public enum ElectionTrusteeRole
{
    InternalTrustee = 0,
    ExternalTrustee = 1,
    AuditorTrustee = 2,
    OwnerTrustee = 3,
}

public enum ElectionTrusteeBackupStatus
{
    NotRequired = 0,
    Registered = 1,
    Missing = 2,
    Failed = 3,
}

public enum ElectionTrusteeExceptionStatus
{
    None = 0,
    Unavailable = 1,
    ReplacedBeforeOpen = 2,
    Compromised = 3,
    Disputed = 4,
}

public enum ElectionTrusteeReleaseArtifactStatus
{
    Accepted = 0,
    Missing = 1,
    Rejected = 2,
}

public record ElectionTrusteeControlDomainRecord(
    Guid Id,
    ElectionId ElectionId,
    string ControlDomainProfileId,
    string ControlDomainProfileVersion,
    string ThresholdProfileId,
    Guid? CeremonyVersionId,
    string TrusteeId,
    string TrusteeAccountId,
    string TrusteePersonRef,
    ElectionTrusteeRole TrusteeRole,
    string CustodyMode,
    string CustodyDomainRefHash,
    string AdminDomainRefHash,
    string? LegalEntityRefHash,
    string PublicKeyCommitmentHash,
    DateTime AcceptedAt,
    bool AcceptedBeforeOpen,
    ElectionTrusteeBackupStatus BackupStatus,
    ElectionTrusteeExceptionStatus ExceptionStatus,
    ElectionTrusteeControlDomainEvidenceStatus EvidenceStatus,
    string? EvidenceFailureCode,
    string? EvidenceFailureReason,
    DateTime RecordedAt,
    string RecordedByPublicAddress,
    Guid? SourceTransactionId,
    long? SourceBlockHeight,
    Guid? SourceBlockId)
{
    public string ControlDomainProfileId { get; init; } =
        NormalizeRequiredValue(ControlDomainProfileId, nameof(ControlDomainProfileId));

    public string ControlDomainProfileVersion { get; init; } =
        NormalizeRequiredValue(ControlDomainProfileVersion, nameof(ControlDomainProfileVersion));

    public string ThresholdProfileId { get; init; } =
        NormalizeRequiredValue(ThresholdProfileId, nameof(ThresholdProfileId));

    public string TrusteeId { get; init; } =
        NormalizeRequiredValue(TrusteeId, nameof(TrusteeId));

    public string TrusteeAccountId { get; init; } =
        NormalizeRequiredValue(TrusteeAccountId, nameof(TrusteeAccountId));

    public string TrusteePersonRef { get; init; } =
        NormalizeRequiredValue(TrusteePersonRef, nameof(TrusteePersonRef));

    public string CustodyMode { get; init; } =
        NormalizeRequiredValue(CustodyMode, nameof(CustodyMode));

    public string CustodyDomainRefHash { get; init; } =
        NormalizeRequiredValue(CustodyDomainRefHash, nameof(CustodyDomainRefHash));

    public string AdminDomainRefHash { get; init; } =
        NormalizeRequiredValue(AdminDomainRefHash, nameof(AdminDomainRefHash));

    public string? LegalEntityRefHash { get; init; } =
        NormalizeOptionalValue(LegalEntityRefHash);

    public string PublicKeyCommitmentHash { get; init; } =
        NormalizeRequiredValue(PublicKeyCommitmentHash, nameof(PublicKeyCommitmentHash));

    public string? EvidenceFailureCode { get; init; } =
        NormalizeOptionalValue(EvidenceFailureCode);

    public string? EvidenceFailureReason { get; init; } =
        NormalizeOptionalValue(EvidenceFailureReason);

    public string RecordedByPublicAddress { get; init; } =
        NormalizeRequiredValue(RecordedByPublicAddress, nameof(RecordedByPublicAddress));

    internal static string NormalizeRequiredValue(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", paramName);
        }

        return value.Trim();
    }

    internal static string? NormalizeOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public record ElectionTrusteeControlDomainSummaryRecord(
    ElectionId ElectionId,
    string ControlDomainProfileId,
    string ControlDomainProfileVersion,
    string ThresholdProfileId,
    int RequiredTrusteeCount,
    int RequiredThreshold,
    int AcceptedBeforeOpenCount,
    int CompleteEvidenceCount,
    int MissingEvidenceCount,
    int StaleEvidenceCount,
    int IncompatibleEvidenceCount,
    bool IsReadyForOpen,
    IReadOnlyList<ElectionTrusteeControlDomainSummaryRowRecord> Trustees,
    IReadOnlyList<ElectionTrusteeControlDomainReadinessBlockerRecord> ReadinessBlockers)
{
    public string ControlDomainProfileId { get; init; } =
        ElectionTrusteeControlDomainRecord.NormalizeRequiredValue(
            ControlDomainProfileId,
            nameof(ControlDomainProfileId));

    public string ControlDomainProfileVersion { get; init; } =
        ElectionTrusteeControlDomainRecord.NormalizeRequiredValue(
            ControlDomainProfileVersion,
            nameof(ControlDomainProfileVersion));

    public string ThresholdProfileId { get; init; } =
        ElectionTrusteeControlDomainRecord.NormalizeRequiredValue(
            ThresholdProfileId,
            nameof(ThresholdProfileId));

    public IReadOnlyList<ElectionTrusteeControlDomainSummaryRowRecord> Trustees { get; init; } =
        Trustees?.ToArray() ?? Array.Empty<ElectionTrusteeControlDomainSummaryRowRecord>();

    public IReadOnlyList<ElectionTrusteeControlDomainReadinessBlockerRecord> ReadinessBlockers { get; init; } =
        ReadinessBlockers?.ToArray() ?? Array.Empty<ElectionTrusteeControlDomainReadinessBlockerRecord>();
}

public record ElectionTrusteeControlDomainSummaryRowRecord(
    string TrusteeId,
    string TrusteePseudonym,
    ElectionTrusteeControlDomainEvidenceStatus EvidenceStatus,
    bool AcceptedBeforeOpen,
    DateTime? AcceptedAt,
    string? PublicKeyCommitmentHash,
    string? CustodyDomainEvidenceHash,
    string? AdminDomainEvidenceHash,
    ElectionTrusteeBackupStatus BackupStatus,
    ElectionTrusteeExceptionStatus ExceptionStatus,
    string? FailureCode)
{
    public string TrusteeId { get; init; } =
        ElectionTrusteeControlDomainRecord.NormalizeRequiredValue(TrusteeId, nameof(TrusteeId));

    public string TrusteePseudonym { get; init; } =
        ElectionTrusteeControlDomainRecord.NormalizeRequiredValue(TrusteePseudonym, nameof(TrusteePseudonym));

    public string? PublicKeyCommitmentHash { get; init; } =
        ElectionTrusteeControlDomainRecord.NormalizeOptionalValue(PublicKeyCommitmentHash);

    public string? CustodyDomainEvidenceHash { get; init; } =
        ElectionTrusteeControlDomainRecord.NormalizeOptionalValue(CustodyDomainEvidenceHash);

    public string? AdminDomainEvidenceHash { get; init; } =
        ElectionTrusteeControlDomainRecord.NormalizeOptionalValue(AdminDomainEvidenceHash);

    public string? FailureCode { get; init; } =
        ElectionTrusteeControlDomainRecord.NormalizeOptionalValue(FailureCode);
}

public record ElectionTrusteeControlDomainReadinessBlockerRecord(
    string Code,
    string Message,
    string? TrusteeId,
    bool BlocksOpen,
    bool BlocksFinalization)
{
    public string Code { get; init; } =
        ElectionTrusteeControlDomainRecord.NormalizeRequiredValue(Code, nameof(Code));

    public string Message { get; init; } =
        ElectionTrusteeControlDomainRecord.NormalizeRequiredValue(Message, nameof(Message));

    public string? TrusteeId { get; init; } =
        ElectionTrusteeControlDomainRecord.NormalizeOptionalValue(TrusteeId);
}

public record ElectionTrusteeReleaseArtifactRecord(
    Guid Id,
    ElectionId ElectionId,
    Guid FinalizationSessionId,
    string ControlDomainProfileId,
    string ThresholdProfileId,
    string TrusteeId,
    string TrusteePseudonym,
    ElectionTrusteeReleaseArtifactStatus Status,
    string? ShareMaterialHash,
    string? ArtifactHash,
    string? FailureCode,
    string? FailureReason,
    Guid CloseArtifactId,
    byte[] AcceptedBallotSetHash,
    byte[] FinalEncryptedTallyHash,
    string TargetTallyId,
    Guid? CeremonyVersionId,
    string? TallyPublicKeyFingerprint,
    string? ExecutorSessionPublicKeyHash,
    string? ExecutorKeyAlgorithm,
    DateTime RecordedAt)
{
    public string ControlDomainProfileId { get; init; } =
        ElectionTrusteeControlDomainRecord.NormalizeRequiredValue(
            ControlDomainProfileId,
            nameof(ControlDomainProfileId));

    public string ThresholdProfileId { get; init; } =
        ElectionTrusteeControlDomainRecord.NormalizeRequiredValue(
            ThresholdProfileId,
            nameof(ThresholdProfileId));

    public string TrusteeId { get; init; } =
        ElectionTrusteeControlDomainRecord.NormalizeRequiredValue(TrusteeId, nameof(TrusteeId));

    public string TrusteePseudonym { get; init; } =
        ElectionTrusteeControlDomainRecord.NormalizeRequiredValue(TrusteePseudonym, nameof(TrusteePseudonym));

    public string? ShareMaterialHash { get; init; } =
        ElectionTrusteeControlDomainRecord.NormalizeOptionalValue(ShareMaterialHash);

    public string? ArtifactHash { get; init; } =
        ElectionTrusteeControlDomainRecord.NormalizeOptionalValue(ArtifactHash);

    public string? FailureCode { get; init; } =
        ElectionTrusteeControlDomainRecord.NormalizeOptionalValue(FailureCode);

    public string? FailureReason { get; init; } =
        ElectionTrusteeControlDomainRecord.NormalizeOptionalValue(FailureReason);

    public byte[] AcceptedBallotSetHash { get; init; } =
        AcceptedBallotSetHash is { Length: > 0 }
            ? AcceptedBallotSetHash.ToArray()
            : throw new ArgumentException("Accepted ballot set hash is required.", nameof(AcceptedBallotSetHash));

    public byte[] FinalEncryptedTallyHash { get; init; } =
        FinalEncryptedTallyHash is { Length: > 0 }
            ? FinalEncryptedTallyHash.ToArray()
            : throw new ArgumentException("Final encrypted tally hash is required.", nameof(FinalEncryptedTallyHash));

    public string TargetTallyId { get; init; } =
        ElectionTrusteeControlDomainRecord.NormalizeRequiredValue(TargetTallyId, nameof(TargetTallyId));

    public string? TallyPublicKeyFingerprint { get; init; } =
        ElectionTrusteeControlDomainRecord.NormalizeOptionalValue(TallyPublicKeyFingerprint);

    public string? ExecutorSessionPublicKeyHash { get; init; } =
        ElectionTrusteeControlDomainRecord.NormalizeOptionalValue(ExecutorSessionPublicKeyHash);

    public string? ExecutorKeyAlgorithm { get; init; } =
        ElectionTrusteeControlDomainRecord.NormalizeOptionalValue(ExecutorKeyAlgorithm);
}
