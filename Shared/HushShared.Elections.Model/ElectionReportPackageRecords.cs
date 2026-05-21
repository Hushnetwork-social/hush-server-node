namespace HushShared.Elections.Model;

public record ElectionReportPackageRecord(
    Guid Id,
    ElectionId ElectionId,
    int AttemptNumber,
    Guid? PreviousAttemptId,
    Guid? FinalizationSessionId,
    Guid? TallyReadyArtifactId,
    Guid? UnofficialResultArtifactId,
    Guid? OfficialResultArtifactId,
    Guid? FinalizeArtifactId,
    Guid? CloseBoundaryArtifactId,
    Guid? CloseEligibilitySnapshotId,
    Guid? FinalizationReleaseEvidenceId,
    ElectionReportPackageStatus Status,
    byte[] FrozenEvidenceHash,
    string FrozenEvidenceFingerprint,
    byte[]? PackageHash,
    int ArtifactCount,
    string? FailureCode,
    string? FailureReason,
    DateTime AttemptedAt,
    DateTime? SealedAt,
    string AttemptedByPublicAddress,
    ElectionReportPackageKind PackageKind = ElectionReportPackageKind.FinalResult,
    Guid? VoidDecisionId = null,
    Guid? VoidPublicationAttemptId = null,
    Guid? SupersededByVoidDecisionId = null,
    DateTime? SupersededAt = null)
{
    public byte[] FrozenEvidenceHash { get; init; } =
        CloneRequiredBytes(FrozenEvidenceHash, nameof(FrozenEvidenceHash));

    public string FrozenEvidenceFingerprint { get; init; } =
        NormalizeRequiredValue(FrozenEvidenceFingerprint, nameof(FrozenEvidenceFingerprint));

    public byte[]? PackageHash { get; init; } =
        CloneOptionalBytes(PackageHash);

    public string? FailureCode { get; init; } =
        NormalizeOptionalValue(FailureCode);

    public string? FailureReason { get; init; } =
        NormalizeOptionalValue(FailureReason);

    public string AttemptedByPublicAddress { get; init; } =
        NormalizeRequiredValue(AttemptedByPublicAddress, nameof(AttemptedByPublicAddress));

    public ElectionReportPackageStatus Status { get; init; } =
        ValidatePackageStatus(
            Status,
            PackageKind,
            TallyReadyArtifactId,
            UnofficialResultArtifactId,
            VoidDecisionId,
            SupersededByVoidDecisionId,
            SupersededAt);

    public ElectionReportPackageRecord SupersedeByVoid(Guid voidDecisionId, DateTime? supersededAt = null) =>
        this with
        {
            Status = ElectionReportPackageStatus.SupersededByVoid,
            SupersededByVoidDecisionId = voidDecisionId,
            SupersededAt = supersededAt ?? DateTime.UtcNow,
        };

    private static ElectionReportPackageStatus ValidatePackageStatus(
        ElectionReportPackageStatus status,
        ElectionReportPackageKind packageKind,
        Guid? tallyReadyArtifactId,
        Guid? unofficialResultArtifactId,
        Guid? voidDecisionId,
        Guid? supersededByVoidDecisionId,
        DateTime? supersededAt)
    {
        if (packageKind == ElectionReportPackageKind.FinalResult &&
            (tallyReadyArtifactId is null || unofficialResultArtifactId is null))
        {
            throw new ArgumentException(
                "Final-result report packages require tally-ready and unofficial-result artifact ids.");
        }

        if (packageKind == ElectionReportPackageKind.Void && voidDecisionId is null)
        {
            throw new ArgumentException("Void report packages require a void decision id.", nameof(VoidDecisionId));
        }

        if (status == ElectionReportPackageStatus.SupersededByVoid &&
            (supersededByVoidDecisionId is null || supersededAt is null))
        {
            throw new ArgumentException(
                "Superseded packages require the void decision id and superseded timestamp.");
        }

        return status;
    }

    private static byte[] CloneRequiredBytes(byte[]? value, string paramName)
    {
        if (value is not { Length: > 0 })
        {
            throw new ArgumentException("Value is required.", paramName);
        }

        return value.ToArray();
    }

    private static byte[]? CloneOptionalBytes(byte[]? value) =>
        value is null ? null : value.ToArray();

    private static string NormalizeRequiredValue(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", paramName);
        }

        return value.Trim();
    }

    private static string? NormalizeOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public record ElectionReportArtifactRecord(
    Guid Id,
    Guid ReportPackageId,
    ElectionId ElectionId,
    ElectionReportArtifactKind ArtifactKind,
    ElectionReportArtifactFormat Format,
    ElectionReportArtifactAccessScope AccessScope,
    int SortOrder,
    string Title,
    string FileName,
    string MediaType,
    byte[] ContentHash,
    string Content,
    Guid? PairedArtifactId,
    DateTime RecordedAt)
{
    public string Title { get; init; } =
        NormalizeRequiredValue(Title, nameof(Title));

    public string FileName { get; init; } =
        NormalizeRequiredValue(FileName, nameof(FileName));

    public string MediaType { get; init; } =
        NormalizeRequiredValue(MediaType, nameof(MediaType));

    public byte[] ContentHash { get; init; } =
        CloneRequiredBytes(ContentHash, nameof(ContentHash));

    public string Content { get; init; } =
        NormalizeRequiredContent(Content, nameof(Content));

    private static byte[] CloneRequiredBytes(byte[]? value, string paramName)
    {
        if (value is not { Length: > 0 })
        {
            throw new ArgumentException("Value is required.", paramName);
        }

        return value.ToArray();
    }

    private static string NormalizeRequiredValue(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", paramName);
        }

        return value.Trim();
    }

    private static string NormalizeRequiredContent(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Content is required.", paramName);
        }

        return value;
    }
}

public record ElectionReportAccessGrantRecord(
    Guid Id,
    ElectionId ElectionId,
    string ActorPublicAddress,
    ElectionReportAccessGrantRole GrantRole,
    DateTime GrantedAt,
    string GrantedByPublicAddress)
{
    public string ActorPublicAddress { get; init; } =
        NormalizeRequiredValue(ActorPublicAddress, nameof(ActorPublicAddress));

    public string GrantedByPublicAddress { get; init; } =
        NormalizeRequiredValue(GrantedByPublicAddress, nameof(GrantedByPublicAddress));

    private static string NormalizeRequiredValue(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", paramName);
        }

        return value.Trim();
    }
}
