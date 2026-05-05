using HushNode.Elections.Storage;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public sealed class ProtocolPackageBindingService
{
    public async Task<ProtocolPackageBindingRecord?> CreateInitialDraftBindingAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        string actorPublicAddress,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(election);

        var latestCatalogEntry = await repository.GetLatestApprovedProtocolPackageCatalogEntryAsync(election.SelectedProfileId);
        if (latestCatalogEntry is null)
        {
            return null;
        }

        var binding = ElectionModelFactory.CreateProtocolPackageBindingFromCatalog(
            election.ElectionId,
            latestCatalogEntry,
            election.SelectedProfileId,
            election.CurrentDraftRevision,
            actorPublicAddress,
            sourceTransactionId: sourceTransactionId,
            sourceBlockHeight: sourceBlockHeight,
            sourceBlockId: sourceBlockId);

        await repository.SaveProtocolPackageBindingAsync(binding);
        return binding;
    }

    public async Task<ProtocolPackageBindingRecord?> MarkDraftBindingDriftAsync(
        IElectionsRepository repository,
        ElectionRecord updatedElection,
        string actorPublicAddress)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(updatedElection);

        var binding = await repository.GetLatestProtocolPackageBindingAsync(updatedElection.ElectionId);
        if (binding is null || binding.Status is ProtocolPackageBindingStatus.Sealed or ProtocolPackageBindingStatus.ReferenceOnly)
        {
            return binding;
        }

        var now = DateTime.UtcNow;
        if (!string.Equals(binding.SelectedProfileId, updatedElection.SelectedProfileId, StringComparison.Ordinal))
        {
            var incompatible = binding.MarkIncompatible(now, actorPublicAddress) with
            {
                DraftRevision = updatedElection.CurrentDraftRevision,
            };
            await repository.UpdateProtocolPackageBindingAsync(incompatible);
            return incompatible;
        }

        var latestCatalogEntry = await repository.GetLatestApprovedProtocolPackageCatalogEntryAsync(updatedElection.SelectedProfileId);
        if (latestCatalogEntry is not null &&
            !BindingMatchesCatalog(binding, latestCatalogEntry) &&
            binding.Status != ProtocolPackageBindingStatus.Stale)
        {
            var stale = binding.MarkStale(now, actorPublicAddress) with
            {
                DraftRevision = updatedElection.CurrentDraftRevision,
            };
            await repository.UpdateProtocolPackageBindingAsync(stale);
            return stale;
        }

        return binding;
    }

    public async Task<ProtocolPackageBindingRefreshOutcome> RefreshDraftBindingAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        string actorPublicAddress,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(election);

        if (election.LifecycleState != ElectionLifecycleState.Draft)
        {
            return ProtocolPackageBindingRefreshOutcome.Failure(
                ElectionCommandErrorCode.InvalidState,
                "Protocol package refs can only be refreshed while the election is in draft.");
        }

        if (!string.Equals(election.OwnerPublicAddress, actorPublicAddress, StringComparison.Ordinal))
        {
            return ProtocolPackageBindingRefreshOutcome.Failure(
                ElectionCommandErrorCode.Forbidden,
                "Only the owner can refresh protocol package refs.");
        }

        var latestCatalogEntry = await repository.GetLatestApprovedProtocolPackageCatalogEntryAsync(election.SelectedProfileId);
        if (latestCatalogEntry is null)
        {
            return ProtocolPackageBindingRefreshOutcome.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                $"No approved Protocol Omega package is available for selected profile {election.SelectedProfileId}.");
        }

        var current = await repository.GetLatestProtocolPackageBindingAsync(election.ElectionId);
        var refreshed = current is null
            ? ElectionModelFactory.CreateProtocolPackageBindingFromCatalog(
                election.ElectionId,
                latestCatalogEntry,
                election.SelectedProfileId,
                election.CurrentDraftRevision,
                actorPublicAddress,
                source: ProtocolPackageBindingSource.OwnerRefresh,
                sourceTransactionId: sourceTransactionId,
                sourceBlockHeight: sourceBlockHeight,
                sourceBlockId: sourceBlockId)
            : current.RefreshFromCatalog(
                latestCatalogEntry,
                election.SelectedProfileId,
                election.CurrentDraftRevision,
                actorPublicAddress,
                DateTime.UtcNow,
                ProtocolPackageBindingSource.OwnerRefresh,
                sourceTransactionId,
                sourceBlockHeight,
                sourceBlockId);

        await repository.SaveProtocolPackageBindingAsync(refreshed);
        return ProtocolPackageBindingRefreshOutcome.Success(refreshed);
    }

    public async Task<ProtocolPackageBindingOpenValidation> ValidateForOpenAsync(
        IElectionsRepository repository,
        ElectionRecord election)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(election);

        var binding = await repository.GetLatestProtocolPackageBindingAsync(election.ElectionId);
        if (binding is null)
        {
            return ProtocolPackageBindingOpenValidation.NotReady(
                ProtocolPackageBindingStatus.Missing,
                null,
                "Latest approved Protocol Omega package refs are missing. Refresh the protocol package binding before opening the election.");
        }

        if (!string.Equals(binding.SelectedProfileId, election.SelectedProfileId, StringComparison.Ordinal))
        {
            return ProtocolPackageBindingOpenValidation.NotReady(
                ProtocolPackageBindingStatus.Incompatible,
                binding,
                "Protocol Omega package refs are incompatible with the selected circuit/profile. Refresh the protocol package binding before opening the election.");
        }

        var latestCatalogEntry = await repository.GetLatestApprovedProtocolPackageCatalogEntryAsync(election.SelectedProfileId);
        if (latestCatalogEntry is null)
        {
            return ProtocolPackageBindingOpenValidation.NotReady(
                ProtocolPackageBindingStatus.Missing,
                binding,
                $"No approved Protocol Omega package is available for selected profile {election.SelectedProfileId}.");
        }

        if (!BindingMatchesCatalog(binding, latestCatalogEntry))
        {
            return ProtocolPackageBindingOpenValidation.NotReady(
                ProtocolPackageBindingStatus.Stale,
                binding,
                "Protocol Omega package refs are stale. Refresh to the latest approved compatible package before opening the election.");
        }

        if (binding.Status != ProtocolPackageBindingStatus.Latest)
        {
            return ProtocolPackageBindingOpenValidation.NotReady(
                binding.Status,
                binding,
                $"Protocol Omega package refs are {binding.Status}. Refresh to the latest approved compatible package before opening the election.");
        }

        return ProtocolPackageBindingOpenValidation.Ready(binding);
    }

    public async Task<ProtocolPackageBindingRecord> SealLatestBindingForOpenAsync(
        IElectionsRepository repository,
        ProtocolPackageBindingRecord binding,
        DateTime sealedAt,
        string sealedByPublicAddress,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(binding);

        var sealedBinding = binding.SealAtOpen(
            sealedAt,
            sealedByPublicAddress,
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);
        await repository.UpdateProtocolPackageBindingAsync(sealedBinding);
        return sealedBinding;
    }

    private static bool BindingMatchesCatalog(
        ProtocolPackageBindingRecord binding,
        ApprovedProtocolPackageCatalogEntryRecord catalogEntry) =>
        catalogEntry.IsApprovedForElectionOpen &&
        catalogEntry.IsCompatibleWithProfile(binding.SelectedProfileId) &&
        string.Equals(binding.PackageId, catalogEntry.PackageId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(binding.PackageVersion, catalogEntry.PackageVersion, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(binding.SpecPackageHash, catalogEntry.SpecPackageHash, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(binding.ProofPackageHash, catalogEntry.ProofPackageHash, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(binding.ReleaseManifestHash, catalogEntry.ReleaseManifestHash, StringComparison.OrdinalIgnoreCase);
}

public sealed record ProtocolPackageBindingOpenValidation(
    bool IsReady,
    ProtocolPackageBindingStatus Status,
    ProtocolPackageBindingRecord? Binding,
    string? ErrorMessage)
{
    public static ProtocolPackageBindingOpenValidation Ready(ProtocolPackageBindingRecord binding) =>
        new(true, ProtocolPackageBindingStatus.Latest, binding, null);

    public static ProtocolPackageBindingOpenValidation NotReady(
        ProtocolPackageBindingStatus status,
        ProtocolPackageBindingRecord? binding,
        string errorMessage) =>
        new(false, status, binding, errorMessage);
}

public sealed record ProtocolPackageBindingRefreshOutcome(
    bool IsSuccess,
    ElectionCommandErrorCode ErrorCode,
    string? ErrorMessage,
    ProtocolPackageBindingRecord? Binding)
{
    public static ProtocolPackageBindingRefreshOutcome Success(ProtocolPackageBindingRecord binding) =>
        new(true, ElectionCommandErrorCode.None, null, binding);

    public static ProtocolPackageBindingRefreshOutcome Failure(
        ElectionCommandErrorCode errorCode,
        string errorMessage) =>
        new(false, errorCode, errorMessage, null);
}
