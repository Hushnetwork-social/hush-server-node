using HushShared.Elections.Model;

namespace HushShared.Elections.Verification.Model;

public record ElectionSp06ControlProfileArtifactRecord(
    string ElectionId,
    string ControlDomainProfileId,
    string ControlDomainProfileVersion,
    string ThresholdProfileId,
    int TrusteeCount,
    int TrusteeThreshold,
    bool HighAssuranceClaimed,
    IReadOnlyList<string> AllowedCustodyModes,
    IReadOnlyList<string> PublicPrivacyBoundary)
{
    public string ElectionId { get; init; } = NormalizeRequiredValue(ElectionId, nameof(ElectionId));

    public string ControlDomainProfileId { get; init; } =
        NormalizeRequiredValue(ControlDomainProfileId, nameof(ControlDomainProfileId));

    public string ControlDomainProfileVersion { get; init; } =
        NormalizeRequiredValue(ControlDomainProfileVersion, nameof(ControlDomainProfileVersion));

    public string ThresholdProfileId { get; init; } =
        NormalizeRequiredValue(ThresholdProfileId, nameof(ThresholdProfileId));

    public IReadOnlyList<string> AllowedCustodyModes { get; init; } =
        NormalizeStringList(AllowedCustodyModes);

    public IReadOnlyList<string> PublicPrivacyBoundary { get; init; } =
        NormalizeStringList(PublicPrivacyBoundary);

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

    internal static IReadOnlyList<string> NormalizeStringList(IReadOnlyList<string>? values) =>
        values is null
            ? Array.Empty<string>()
            : values
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
}

public record ElectionSp06TrusteeControlSummaryArtifactRecord(
    string ElectionId,
    string ControlDomainProfileId,
    string ControlDomainProfileVersion,
    string ThresholdProfileId,
    int TrusteeCount,
    int TrusteeThreshold,
    int AcceptedBeforeOpenCount,
    int CompleteEvidenceCount,
    int MissingEvidenceCount,
    int StaleEvidenceCount,
    int IncompatibleEvidenceCount,
    int AcceptedReleaseArtifactCount,
    int MissingReleaseArtifactCount,
    int RejectedReleaseArtifactCount,
    string FinalEncryptedTallyHash,
    string TargetTallyId,
    string? ExecutorSessionPublicKeyHash,
    string? ExecutorKeyAlgorithm,
    IReadOnlyList<ElectionSp06TrusteeControlSummaryRowArtifactRecord> Trustees,
    IReadOnlyList<ElectionSp06ReadinessBlockerArtifactRecord> ReadinessBlockers,
    IReadOnlyList<string> PublicPrivacyBoundary)
{
    public string ElectionId { get; init; } =
        ElectionSp06ControlProfileArtifactRecord.NormalizeRequiredValue(ElectionId, nameof(ElectionId));

    public string ControlDomainProfileId { get; init; } =
        ElectionSp06ControlProfileArtifactRecord.NormalizeRequiredValue(
            ControlDomainProfileId,
            nameof(ControlDomainProfileId));

    public string ControlDomainProfileVersion { get; init; } =
        ElectionSp06ControlProfileArtifactRecord.NormalizeRequiredValue(
            ControlDomainProfileVersion,
            nameof(ControlDomainProfileVersion));

    public string ThresholdProfileId { get; init; } =
        ElectionSp06ControlProfileArtifactRecord.NormalizeRequiredValue(
            ThresholdProfileId,
            nameof(ThresholdProfileId));

    public string FinalEncryptedTallyHash { get; init; } =
        ElectionSp06ControlProfileArtifactRecord.NormalizeRequiredValue(
            FinalEncryptedTallyHash,
            nameof(FinalEncryptedTallyHash));

    public string TargetTallyId { get; init; } =
        ElectionSp06ControlProfileArtifactRecord.NormalizeRequiredValue(TargetTallyId, nameof(TargetTallyId));

    public string? ExecutorSessionPublicKeyHash { get; init; } =
        ElectionSp06ControlProfileArtifactRecord.NormalizeOptionalValue(ExecutorSessionPublicKeyHash);

    public string? ExecutorKeyAlgorithm { get; init; } =
        ElectionSp06ControlProfileArtifactRecord.NormalizeOptionalValue(ExecutorKeyAlgorithm);

    public IReadOnlyList<ElectionSp06TrusteeControlSummaryRowArtifactRecord> Trustees { get; init; } =
        Trustees?.ToArray() ?? Array.Empty<ElectionSp06TrusteeControlSummaryRowArtifactRecord>();

    public IReadOnlyList<ElectionSp06ReadinessBlockerArtifactRecord> ReadinessBlockers { get; init; } =
        ReadinessBlockers?.ToArray() ?? Array.Empty<ElectionSp06ReadinessBlockerArtifactRecord>();

    public IReadOnlyList<string> PublicPrivacyBoundary { get; init; } =
        ElectionSp06ControlProfileArtifactRecord.NormalizeStringList(PublicPrivacyBoundary);
}

public record ElectionSp06TrusteeControlSummaryRowArtifactRecord(
    string TrusteeId,
    string TrusteePseudonym,
    ElectionTrusteeControlDomainEvidenceStatus EvidenceStatus,
    ElectionTrusteeReleaseArtifactStatus ReleaseArtifactStatus,
    bool AcceptedBeforeOpen,
    DateTime? AcceptedAt,
    string? PublicKeyCommitmentHash,
    string? CustodyDomainEvidenceHash,
    string? AdminDomainEvidenceHash,
    string? ReleaseArtifactHash,
    string? ShareMaterialHash,
    string? FailureCode)
{
    public string TrusteeId { get; init; } =
        ElectionSp06ControlProfileArtifactRecord.NormalizeRequiredValue(TrusteeId, nameof(TrusteeId));

    public string TrusteePseudonym { get; init; } =
        ElectionSp06ControlProfileArtifactRecord.NormalizeRequiredValue(TrusteePseudonym, nameof(TrusteePseudonym));

    public string? PublicKeyCommitmentHash { get; init; } =
        ElectionSp06ControlProfileArtifactRecord.NormalizeOptionalValue(PublicKeyCommitmentHash);

    public string? CustodyDomainEvidenceHash { get; init; } =
        ElectionSp06ControlProfileArtifactRecord.NormalizeOptionalValue(CustodyDomainEvidenceHash);

    public string? AdminDomainEvidenceHash { get; init; } =
        ElectionSp06ControlProfileArtifactRecord.NormalizeOptionalValue(AdminDomainEvidenceHash);

    public string? ReleaseArtifactHash { get; init; } =
        ElectionSp06ControlProfileArtifactRecord.NormalizeOptionalValue(ReleaseArtifactHash);

    public string? ShareMaterialHash { get; init; } =
        ElectionSp06ControlProfileArtifactRecord.NormalizeOptionalValue(ShareMaterialHash);

    public string? FailureCode { get; init; } =
        ElectionSp06ControlProfileArtifactRecord.NormalizeOptionalValue(FailureCode);
}

public record ElectionSp06ReadinessBlockerArtifactRecord(
    string Code,
    string Message,
    string? TrusteeId,
    bool BlocksOpen,
    bool BlocksFinalization)
{
    public string Code { get; init; } =
        ElectionSp06ControlProfileArtifactRecord.NormalizeRequiredValue(Code, nameof(Code));

    public string Message { get; init; } =
        ElectionSp06ControlProfileArtifactRecord.NormalizeRequiredValue(Message, nameof(Message));

    public string? TrusteeId { get; init; } =
        ElectionSp06ControlProfileArtifactRecord.NormalizeOptionalValue(TrusteeId);
}

public record ElectionSp06VerifierOutputArtifactRecord(
    string ElectionId,
    string VerifierProfileId,
    DateTime VerifiedAt,
    IReadOnlyList<VerifierCheckResultRecord> Results);

public record ElectionSp06RestrictedControlDomainEvidenceArtifactRecord(
    string ElectionId,
    IReadOnlyList<ElectionTrusteeControlDomainRecord> ControlDomains)
{
    public string ElectionId { get; init; } =
        ElectionSp06ControlProfileArtifactRecord.NormalizeRequiredValue(ElectionId, nameof(ElectionId));

    public IReadOnlyList<ElectionTrusteeControlDomainRecord> ControlDomains { get; init; } =
        ControlDomains?.ToArray() ?? Array.Empty<ElectionTrusteeControlDomainRecord>();
}

public record ElectionSp06RestrictedReleaseArtifactEvidenceRecord(
    string ElectionId,
    IReadOnlyList<ElectionTrusteeReleaseArtifactRecord> ReleaseArtifacts)
{
    public string ElectionId { get; init; } =
        ElectionSp06ControlProfileArtifactRecord.NormalizeRequiredValue(ElectionId, nameof(ElectionId));

    public IReadOnlyList<ElectionTrusteeReleaseArtifactRecord> ReleaseArtifacts { get; init; } =
        ReleaseArtifacts?.ToArray() ?? Array.Empty<ElectionTrusteeReleaseArtifactRecord>();
}
