namespace HushShared.Elections.Model;

public record ProtocolPackageFileHashRecord(
    string RelativePath,
    string Sha256Hash,
    long SizeBytes,
    string? MediaType)
{
    public string RelativePath { get; init; } =
        ProtocolPackageRecordValidation.NormalizeRequiredValue(
            RelativePath,
            nameof(RelativePath));

    public string Sha256Hash { get; init; } =
        ProtocolPackageRecordValidation.NormalizeSha256Hash(
            Sha256Hash,
            nameof(Sha256Hash));

    public string? MediaType { get; init; } =
        ProtocolPackageRecordValidation.NormalizeOptionalValue(MediaType);

    public long SizeBytes { get; init; } =
        SizeBytes >= 0
            ? SizeBytes
            : throw new ArgumentOutOfRangeException(nameof(SizeBytes), "File size cannot be negative.");
}

public record ProtocolPackageAccessLocationRecord(
    ProtocolPackageAccessLocationKind LocationKind,
    string Label,
    string Location,
    string? ContentHash)
{
    public string Label { get; init; } =
        ProtocolPackageRecordValidation.NormalizeRequiredValue(
            Label,
            nameof(Label));

    public string Location { get; init; } =
        ProtocolPackageRecordValidation.NormalizeRequiredValue(
            Location,
            nameof(Location));

    public string? ContentHash { get; init; } =
        ProtocolPackageRecordValidation.NormalizeOptionalSha256Hash(
            ContentHash,
            nameof(ContentHash));
}

public record ProtocolPackageManifestRecord(
    string PackageId,
    string PackageVersion,
    ProtocolPackageKind PackageKind,
    ProtocolPackageApprovalStatus PackageStatus,
    string PackageHash,
    string ArchiveFileName,
    string SchemaVersion,
    IReadOnlyList<string> CompatibleProfileIds,
    IReadOnlyList<ProtocolPackageFileHashRecord> Files,
    IReadOnlyList<ProtocolPackageAccessLocationRecord> AccessLocations,
    ProtocolPackageExternalReviewStatus ExternalReviewStatus,
    DateTime GeneratedAt)
{
    public string PackageId { get; init; } =
        ProtocolPackageRecordValidation.NormalizeRequiredValue(
            PackageId,
            nameof(PackageId));

    public string PackageVersion { get; init; } =
        ProtocolPackageRecordValidation.NormalizeRequiredValue(
            PackageVersion,
            nameof(PackageVersion));

    public string PackageHash { get; init; } =
        ProtocolPackageRecordValidation.NormalizeSha256Hash(
            PackageHash,
            nameof(PackageHash));

    public string ArchiveFileName { get; init; } =
        ProtocolPackageRecordValidation.NormalizeRequiredValue(
            ArchiveFileName,
            nameof(ArchiveFileName));

    public string SchemaVersion { get; init; } =
        ProtocolPackageRecordValidation.NormalizeRequiredValue(
            SchemaVersion,
            nameof(SchemaVersion));

    public IReadOnlyList<string> CompatibleProfileIds { get; init; } =
        ProtocolPackageRecordValidation.NormalizeRequiredStringList(
            CompatibleProfileIds,
            nameof(CompatibleProfileIds));

    public IReadOnlyList<ProtocolPackageFileHashRecord> Files { get; init; } =
        ProtocolPackageRecordValidation.NormalizeRequiredRecordList(
            Files,
            nameof(Files));

    public IReadOnlyList<ProtocolPackageAccessLocationRecord> AccessLocations { get; init; } =
        ProtocolPackageRecordValidation.NormalizeRequiredRecordList(
            AccessLocations,
            nameof(AccessLocations));
}

public record ProtocolOmegaPackageReleaseManifestRecord(
    string PackageId,
    string PackageVersion,
    string SpecPackageHash,
    string ProofPackageHash,
    string ReleaseManifestHash,
    ProtocolPackageApprovalStatus ApprovalStatus,
    IReadOnlyList<string> CompatibleProfileIds,
    IReadOnlyList<ProtocolPackageAccessLocationRecord> SpecAccessLocations,
    IReadOnlyList<ProtocolPackageAccessLocationRecord> ProofAccessLocations,
    ProtocolPackageExternalReviewStatus ExternalReviewStatus,
    DateTime ReleasedAt)
{
    private readonly IReadOnlyList<ProtocolPackageFileHashRecord> _releaseFiles =
        Array.Empty<ProtocolPackageFileHashRecord>();

    public string PackageId { get; init; } =
        ProtocolPackageRecordValidation.NormalizeRequiredValue(
            PackageId,
            nameof(PackageId));

    public string PackageVersion { get; init; } =
        ProtocolPackageRecordValidation.NormalizeRequiredValue(
            PackageVersion,
            nameof(PackageVersion));

    public string SpecPackageHash { get; init; } =
        ProtocolPackageRecordValidation.NormalizeSha256Hash(
            SpecPackageHash,
            nameof(SpecPackageHash));

    public string ProofPackageHash { get; init; } =
        ProtocolPackageRecordValidation.NormalizeSha256Hash(
            ProofPackageHash,
            nameof(ProofPackageHash));

    public string ReleaseManifestHash { get; init; } =
        ProtocolPackageRecordValidation.NormalizeSha256Hash(
            ReleaseManifestHash,
            nameof(ReleaseManifestHash));

    public IReadOnlyList<string> CompatibleProfileIds { get; init; } =
        ProtocolPackageRecordValidation.NormalizeRequiredStringList(
            CompatibleProfileIds,
            nameof(CompatibleProfileIds));

    public IReadOnlyList<ProtocolPackageAccessLocationRecord> SpecAccessLocations { get; init; } =
        ProtocolPackageRecordValidation.NormalizeRequiredRecordList(
            SpecAccessLocations,
            nameof(SpecAccessLocations));

    public IReadOnlyList<ProtocolPackageAccessLocationRecord> ProofAccessLocations { get; init; } =
        ProtocolPackageRecordValidation.NormalizeRequiredRecordList(
            ProofAccessLocations,
            nameof(ProofAccessLocations));

    public IReadOnlyList<ProtocolPackageFileHashRecord> ReleaseFiles
    {
        get => _releaseFiles;
        init => _releaseFiles = ProtocolPackageRecordValidation.NormalizeRequiredRecordList(
            value,
            nameof(ReleaseFiles));
    }
}

public record ApprovedProtocolPackageCatalogEntryRecord(
    string PackageId,
    string PackageVersion,
    string SpecPackageHash,
    string ProofPackageHash,
    string ReleaseManifestHash,
    IReadOnlyList<string> CompatibleProfileIds,
    ProtocolPackageApprovalStatus ApprovalStatus,
    DateTime ApprovedAt,
    bool IsLatestForCompatibleProfiles,
    IReadOnlyList<ProtocolPackageAccessLocationRecord> SpecAccessLocations,
    IReadOnlyList<ProtocolPackageAccessLocationRecord> ProofAccessLocations,
    ProtocolPackageExternalReviewStatus ExternalReviewStatus)
{
    public string PackageId { get; init; } =
        ProtocolPackageRecordValidation.NormalizeRequiredValue(
            PackageId,
            nameof(PackageId));

    public string PackageVersion { get; init; } =
        ProtocolPackageRecordValidation.NormalizeRequiredValue(
            PackageVersion,
            nameof(PackageVersion));

    public string SpecPackageHash { get; init; } =
        ProtocolPackageRecordValidation.NormalizeSha256Hash(
            SpecPackageHash,
            nameof(SpecPackageHash));

    public string ProofPackageHash { get; init; } =
        ProtocolPackageRecordValidation.NormalizeSha256Hash(
            ProofPackageHash,
            nameof(ProofPackageHash));

    public string ReleaseManifestHash { get; init; } =
        ProtocolPackageRecordValidation.NormalizeSha256Hash(
            ReleaseManifestHash,
            nameof(ReleaseManifestHash));

    public IReadOnlyList<string> CompatibleProfileIds { get; init; } =
        ProtocolPackageRecordValidation.NormalizeRequiredStringList(
            CompatibleProfileIds,
            nameof(CompatibleProfileIds));

    public IReadOnlyList<ProtocolPackageAccessLocationRecord> SpecAccessLocations { get; init; } =
        ProtocolPackageRecordValidation.NormalizeRequiredRecordList(
            SpecAccessLocations,
            nameof(SpecAccessLocations));

    public IReadOnlyList<ProtocolPackageAccessLocationRecord> ProofAccessLocations { get; init; } =
        ProtocolPackageRecordValidation.NormalizeRequiredRecordList(
            ProofAccessLocations,
            nameof(ProofAccessLocations));

    public bool IsApprovedForElectionOpen =>
        ApprovalStatus == ProtocolPackageApprovalStatus.ApprovedInternal &&
        IsLatestForCompatibleProfiles;

    public bool IsCompatibleWithProfile(string selectedProfileId) =>
        CompatibleProfileIds.Any(
            x => string.Equals(
                x,
                ProtocolPackageRecordValidation.NormalizeRequiredValue(
                    selectedProfileId,
                    nameof(selectedProfileId)),
                StringComparison.OrdinalIgnoreCase));
}
