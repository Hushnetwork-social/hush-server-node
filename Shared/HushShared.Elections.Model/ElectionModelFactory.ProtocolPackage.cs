namespace HushShared.Elections.Model;

public static partial class ElectionModelFactory
{
    public static ProtocolPackageFileHashRecord CreateProtocolPackageFileHash(
        string relativePath,
        string sha256Hash,
        long sizeBytes,
        string? mediaType = null) =>
        new(relativePath, sha256Hash, sizeBytes, mediaType);

    public static ProtocolPackageAccessLocationRecord CreateProtocolPackageAccessLocation(
        ProtocolPackageAccessLocationKind locationKind,
        string label,
        string location,
        string? contentHash = null) =>
        new(locationKind, label, location, contentHash);

    public static ProtocolPackageManifestRecord CreateProtocolPackageManifest(
        string packageId,
        string packageVersion,
        ProtocolPackageKind packageKind,
        ProtocolPackageApprovalStatus packageStatus,
        string packageHash,
        string archiveFileName,
        string schemaVersion,
        IReadOnlyList<string> compatibleProfileIds,
        IReadOnlyList<ProtocolPackageFileHashRecord> files,
        IReadOnlyList<ProtocolPackageAccessLocationRecord> accessLocations,
        ProtocolPackageExternalReviewStatus externalReviewStatus = ProtocolPackageExternalReviewStatus.NotReviewed,
        DateTime? generatedAt = null) =>
        new(
            packageId,
            packageVersion,
            packageKind,
            packageStatus,
            packageHash,
            archiveFileName,
            schemaVersion,
            compatibleProfileIds,
            files,
            accessLocations,
            externalReviewStatus,
            generatedAt ?? DateTime.UtcNow);

    public static ProtocolOmegaPackageReleaseManifestRecord CreateProtocolOmegaPackageReleaseManifest(
        string packageId,
        string packageVersion,
        string specPackageHash,
        string proofPackageHash,
        string releaseManifestHash,
        ProtocolPackageApprovalStatus approvalStatus,
        IReadOnlyList<string> compatibleProfileIds,
        IReadOnlyList<ProtocolPackageAccessLocationRecord> specAccessLocations,
        IReadOnlyList<ProtocolPackageAccessLocationRecord> proofAccessLocations,
        ProtocolPackageExternalReviewStatus externalReviewStatus = ProtocolPackageExternalReviewStatus.NotReviewed,
        DateTime? releasedAt = null) =>
        new(
            packageId,
            packageVersion,
            specPackageHash,
            proofPackageHash,
            releaseManifestHash,
            approvalStatus,
            compatibleProfileIds,
            specAccessLocations,
            proofAccessLocations,
            externalReviewStatus,
            releasedAt ?? DateTime.UtcNow);

    public static ApprovedProtocolPackageCatalogEntryRecord CreateApprovedProtocolPackageCatalogEntry(
        string packageId,
        string packageVersion,
        string specPackageHash,
        string proofPackageHash,
        string releaseManifestHash,
        IReadOnlyList<string> compatibleProfileIds,
        ProtocolPackageApprovalStatus approvalStatus,
        bool isLatestForCompatibleProfiles,
        IReadOnlyList<ProtocolPackageAccessLocationRecord> specAccessLocations,
        IReadOnlyList<ProtocolPackageAccessLocationRecord> proofAccessLocations,
        ProtocolPackageExternalReviewStatus externalReviewStatus = ProtocolPackageExternalReviewStatus.NotReviewed,
        DateTime? approvedAt = null) =>
        new(
            packageId,
            packageVersion,
            specPackageHash,
            proofPackageHash,
            releaseManifestHash,
            compatibleProfileIds,
            approvalStatus,
            approvedAt ?? DateTime.UtcNow,
            isLatestForCompatibleProfiles,
            specAccessLocations,
            proofAccessLocations,
            externalReviewStatus);

    public static ProtocolPackageBindingRecord CreateProtocolPackageBindingFromCatalog(
        ElectionId electionId,
        ApprovedProtocolPackageCatalogEntryRecord catalogEntry,
        string selectedProfileId,
        int draftRevision,
        string boundByPublicAddress,
        DateTime? boundAt = null,
        ProtocolPackageBindingSource source = ProtocolPackageBindingSource.CatalogSelection,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        ArgumentNullException.ThrowIfNull(catalogEntry);

        if (source is ProtocolPackageBindingSource.SealedAtOpen or ProtocolPackageBindingSource.MigrationBackfill)
        {
            throw new ArgumentException("Draft catalog bindings must use catalog-selection or owner-refresh source.", nameof(source));
        }

        if (!catalogEntry.IsCompatibleWithProfile(selectedProfileId))
        {
            throw new ArgumentException("Catalog entry is not compatible with the selected profile.", nameof(catalogEntry));
        }

        return new ProtocolPackageBindingRecord(
            Guid.NewGuid(),
            electionId,
            catalogEntry.PackageId,
            catalogEntry.PackageVersion,
            selectedProfileId,
            catalogEntry.SpecPackageHash,
            catalogEntry.ProofPackageHash,
            catalogEntry.ReleaseManifestHash,
            catalogEntry.SpecAccessLocations,
            catalogEntry.ProofAccessLocations,
            catalogEntry.ApprovalStatus,
            catalogEntry.IsApprovedForElectionOpen
                ? ProtocolPackageBindingStatus.Latest
                : ProtocolPackageBindingStatus.Stale,
            source,
            draftRevision,
            boundAt ?? DateTime.UtcNow,
            SealedAt: null,
            boundByPublicAddress,
            catalogEntry.ExternalReviewStatus,
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);
    }

    public static ProtocolPackageBindingRecord CreateMigrationBackfillProtocolPackageBinding(
        ElectionId electionId,
        ApprovedProtocolPackageCatalogEntryRecord catalogEntry,
        string selectedProfileId,
        int draftRevision,
        string backfilledByPublicAddress,
        DateTime? backfilledAt = null,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        ArgumentNullException.ThrowIfNull(catalogEntry);

        if (!catalogEntry.IsCompatibleWithProfile(selectedProfileId))
        {
            throw new ArgumentException("Catalog entry is not compatible with the selected profile.", nameof(catalogEntry));
        }

        return new ProtocolPackageBindingRecord(
            Guid.NewGuid(),
            electionId,
            catalogEntry.PackageId,
            catalogEntry.PackageVersion,
            selectedProfileId,
            catalogEntry.SpecPackageHash,
            catalogEntry.ProofPackageHash,
            catalogEntry.ReleaseManifestHash,
            catalogEntry.SpecAccessLocations,
            catalogEntry.ProofAccessLocations,
            catalogEntry.ApprovalStatus,
            ProtocolPackageBindingStatus.ReferenceOnly,
            ProtocolPackageBindingSource.MigrationBackfill,
            draftRevision,
            backfilledAt ?? DateTime.UtcNow,
            SealedAt: null,
            backfilledByPublicAddress,
            catalogEntry.ExternalReviewStatus,
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);
    }
}
