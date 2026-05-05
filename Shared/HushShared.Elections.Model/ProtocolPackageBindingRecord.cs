namespace HushShared.Elections.Model;

public record ProtocolPackageBindingRecord(
    Guid Id,
    ElectionId ElectionId,
    string PackageId,
    string PackageVersion,
    string SelectedProfileId,
    string SpecPackageHash,
    string ProofPackageHash,
    string ReleaseManifestHash,
    IReadOnlyList<ProtocolPackageAccessLocationRecord> SpecAccessLocations,
    IReadOnlyList<ProtocolPackageAccessLocationRecord> ProofAccessLocations,
    ProtocolPackageApprovalStatus PackageApprovalStatus,
    ProtocolPackageBindingStatus Status,
    ProtocolPackageBindingSource Source,
    int DraftRevision,
    DateTime BoundAt,
    DateTime? SealedAt,
    string BoundByPublicAddress,
    ProtocolPackageExternalReviewStatus ExternalReviewStatus,
    Guid? SourceTransactionId,
    long? SourceBlockHeight,
    Guid? SourceBlockId)
{
    public string PackageId { get; init; } =
        ProtocolPackageRecordValidation.NormalizeRequiredValue(
            PackageId,
            nameof(PackageId));

    public string PackageVersion { get; init; } =
        ProtocolPackageRecordValidation.NormalizeRequiredValue(
            PackageVersion,
            nameof(PackageVersion));

    public string SelectedProfileId { get; init; } =
        ProtocolPackageRecordValidation.NormalizeRequiredValue(
            SelectedProfileId,
            nameof(SelectedProfileId));

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

    public IReadOnlyList<ProtocolPackageAccessLocationRecord> SpecAccessLocations { get; init; } =
        ProtocolPackageRecordValidation.NormalizeRequiredRecordList(
            SpecAccessLocations,
            nameof(SpecAccessLocations));

    public IReadOnlyList<ProtocolPackageAccessLocationRecord> ProofAccessLocations { get; init; } =
        ProtocolPackageRecordValidation.NormalizeRequiredRecordList(
            ProofAccessLocations,
            nameof(ProofAccessLocations));

    public ProtocolPackageBindingStatus Status { get; init; } =
        NormalizeBindingStatus(Status, Source, SealedAt);

    public int DraftRevision { get; init; } =
        DraftRevision >= 1
            ? DraftRevision
            : throw new ArgumentOutOfRangeException(nameof(DraftRevision), "Draft revision must be at least 1.");

    public string BoundByPublicAddress { get; init; } =
        ProtocolPackageRecordValidation.NormalizeRequiredValue(
            BoundByPublicAddress,
            nameof(BoundByPublicAddress));

    public bool BlocksElectionOpen =>
        Status is
            ProtocolPackageBindingStatus.Missing or
            ProtocolPackageBindingStatus.Stale or
            ProtocolPackageBindingStatus.Incompatible or
            ProtocolPackageBindingStatus.ReferenceOnly;

    public ProtocolPackageBindingRecord SealAtOpen(
        DateTime sealedAt,
        string sealedByPublicAddress,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        EnsureCanChange();

        if (Status != ProtocolPackageBindingStatus.Latest)
        {
            throw new InvalidOperationException("Only a latest protocol package binding can be sealed at open.");
        }

        return this with
        {
            Status = ProtocolPackageBindingStatus.Sealed,
            Source = ProtocolPackageBindingSource.SealedAtOpen,
            SealedAt = sealedAt,
            BoundAt = sealedAt,
            BoundByPublicAddress = ProtocolPackageRecordValidation.NormalizeRequiredValue(
                sealedByPublicAddress,
                nameof(sealedByPublicAddress)),
            SourceTransactionId = sourceTransactionId,
            SourceBlockHeight = sourceBlockHeight,
            SourceBlockId = sourceBlockId,
        };
    }

    public ProtocolPackageBindingRecord RefreshFromCatalog(
        ApprovedProtocolPackageCatalogEntryRecord catalogEntry,
        string selectedProfileId,
        int draftRevision,
        string refreshedByPublicAddress,
        DateTime refreshedAt,
        ProtocolPackageBindingSource source = ProtocolPackageBindingSource.OwnerRefresh,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        EnsureCanChange();

        return ElectionModelFactory.CreateProtocolPackageBindingFromCatalog(
            ElectionId,
            catalogEntry,
            selectedProfileId,
            draftRevision,
            refreshedByPublicAddress,
            refreshedAt,
            source,
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);
    }

    public ProtocolPackageBindingRecord MarkStale(DateTime markedAt, string markedByPublicAddress)
    {
        EnsureCanChange();

        return this with
        {
            Status = ProtocolPackageBindingStatus.Stale,
            BoundAt = markedAt,
            BoundByPublicAddress = ProtocolPackageRecordValidation.NormalizeRequiredValue(
                markedByPublicAddress,
                nameof(markedByPublicAddress)),
        };
    }

    public ProtocolPackageBindingRecord MarkIncompatible(DateTime markedAt, string markedByPublicAddress)
    {
        EnsureCanChange();

        return this with
        {
            Status = ProtocolPackageBindingStatus.Incompatible,
            BoundAt = markedAt,
            BoundByPublicAddress = ProtocolPackageRecordValidation.NormalizeRequiredValue(
                markedByPublicAddress,
                nameof(markedByPublicAddress)),
        };
    }

    private void EnsureCanChange()
    {
        if (Status == ProtocolPackageBindingStatus.Sealed)
        {
            throw new InvalidOperationException("Sealed protocol package refs are immutable.");
        }

        if (Status == ProtocolPackageBindingStatus.ReferenceOnly)
        {
            throw new InvalidOperationException("Reference-only protocol package refs cannot be mutated.");
        }
    }

    private static ProtocolPackageBindingStatus NormalizeBindingStatus(
        ProtocolPackageBindingStatus status,
        ProtocolPackageBindingSource source,
        DateTime? sealedAt)
    {
        ValidateStatusSourceCombination(status, source, sealedAt);
        return status;
    }

    private static void ValidateStatusSourceCombination(
        ProtocolPackageBindingStatus status,
        ProtocolPackageBindingSource source,
        DateTime? sealedAt)
    {
        if (status == ProtocolPackageBindingStatus.Sealed && !sealedAt.HasValue)
        {
            throw new ArgumentException("Sealed package bindings require a sealed timestamp.", nameof(SealedAt));
        }

        if (status != ProtocolPackageBindingStatus.Sealed && sealedAt.HasValue)
        {
            throw new ArgumentException("Only sealed package bindings can carry a sealed timestamp.", nameof(SealedAt));
        }

        if (source == ProtocolPackageBindingSource.SealedAtOpen && status != ProtocolPackageBindingStatus.Sealed)
        {
            throw new ArgumentException("The sealed-at-open source requires sealed binding status.", nameof(Source));
        }

        if (source == ProtocolPackageBindingSource.MigrationBackfill && status != ProtocolPackageBindingStatus.ReferenceOnly)
        {
            throw new ArgumentException("Migration backfill bindings must be reference only.", nameof(Source));
        }
    }
}
