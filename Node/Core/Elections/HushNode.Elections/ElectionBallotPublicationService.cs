using System.Data;
using System.Security.Cryptography;
using System.Text;
using HushNode.Caching;
using HushNode.Elections.Storage;
using HushNode.Events;
using HushShared.Blockchain.BlockModel;
using HushShared.Elections.Model;
using Microsoft.Extensions.Logging;
using Olimpo;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections;

public sealed class ElectionBallotPublicationService(
    IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider,
    IElectionBallotPublicationCryptoService publicationCryptoService,
    IBlockchainCache blockchainCache,
    ElectionBallotPublicationOptions options,
    ILogger<ElectionBallotPublicationService> logger) :
    IElectionBallotPublicationService,
    IHandleAsync<BlockIndexCompletedEvent>
{
    private readonly IUnitOfWorkProvider<ElectionsDbContext> _unitOfWorkProvider = unitOfWorkProvider;
    private readonly IElectionBallotPublicationCryptoService _publicationCryptoService = publicationCryptoService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly ElectionBallotPublicationOptions _options = options;
    private readonly ILogger<ElectionBallotPublicationService> _logger = logger;

    public Task HandleAsync(BlockIndexCompletedEvent message) =>
        ProcessPendingPublicationAsync(message.BlockIndex);

    public async Task ProcessPendingPublicationAsync(BlockIndex blockIndex)
    {
        _options.Validate();

        using var discoveryUnitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var discoveryRepository = discoveryUnitOfWork.GetRepository<IElectionsRepository>();
        var electionIds = (await discoveryRepository.GetElectionIdsWithBallotMemPoolEntriesAsync())
            .Concat(await discoveryRepository.GetClosedElectionIdsAwaitingTallyReadyAsync())
            .Distinct()
            .ToArray();

        foreach (var electionId in electionIds)
        {
            using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
            var repository = unitOfWork.GetRepository<IElectionsRepository>();
            var election = await repository.GetElectionForUpdateAsync(electionId);
            if (election is null || election.LifecycleState == ElectionLifecycleState.Finalized)
            {
                continue;
            }

            var pendingEntries = await repository.GetBallotMemPoolEntriesAsync(electionId);
            if (pendingEntries.Count == 0)
            {
                if (election.LifecycleState == ElectionLifecycleState.Closed && !election.TallyReadyAt.HasValue)
                {
                    await TryMarkTallyReadyAsync(repository, election, blockIndex.Value, _blockchainCache.CurrentBlockId.Value);
                    await unitOfWork.CommitAsync();
                }

                continue;
            }

            var publishCount = ResolvePublishCount(election.LifecycleState, pendingEntries.Count);
            if (publishCount <= 0)
            {
                continue;
            }

            _logger.LogInformation(
                "[ElectionBallotPublicationService] Processing election {ElectionId} in state {LifecycleState} with {PendingCount} queued ballot(s); publishing {PublishCount}.",
                electionId,
                election.LifecycleState,
                pendingEntries.Count,
                publishCount);

            var selectedEntries = SelectEntriesForPublication(pendingEntries, publishCount);
            var existingPublishedBallots = election.LifecycleState == ElectionLifecycleState.Closed
                ? await repository.GetPublishedBallotsAsync(electionId)
                : Array.Empty<ElectionPublishedBallotRecord>();
            var newlyPublishedBallots = new List<ElectionPublishedBallotRecord>(selectedEntries.Count);
            var nextSequence = await repository.GetNextPublishedBallotSequenceAsync(electionId);
            var publishedAt = DateTime.UtcNow;

            foreach (var entry in selectedEntries)
            {
                var acceptedBallot = await repository.GetAcceptedBallotAsync(entry.AcceptedBallotId);
                if (acceptedBallot is null)
                {
                    _logger.LogWarning(
                        "[ElectionBallotPublicationService] Accepted ballot {AcceptedBallotId} was not found for election {ElectionId}; registering replay mismatch.",
                        entry.AcceptedBallotId,
                        electionId);
                    await RegisterIssueAsync(
                        repository,
                        electionId,
                        ElectionPublicationIssueCode.ReplayMismatch,
                        publishedAt,
                        blockIndex.Value,
                        _blockchainCache.CurrentBlockId.Value);
                    continue;
                }

                var publicationPayload = await PreparePublicationPayloadAsync(
                    repository,
                    electionId,
                    acceptedBallot,
                    publishedAt,
                    blockIndex.Value,
                    _blockchainCache.CurrentBlockId.Value);
                var publishedBallot = ElectionModelFactory.CreatePublishedBallotRecord(
                    electionId,
                    nextSequence++,
                    publicationPayload.EncryptedBallotPackage,
                    publicationPayload.ProofBundle,
                    publishedAt,
                    blockIndex.Value,
                    _blockchainCache.CurrentBlockId.Value);

                await repository.SavePublishedBallotAsync(publishedBallot);
                await repository.DeleteBallotMemPoolEntryAsync(entry.Id);
                newlyPublishedBallots.Add(publishedBallot);
            }

            election = election with
            {
                LastUpdatedAt = publishedAt,
            };
            await repository.SaveElectionAsync(election);

            if (election.LifecycleState == ElectionLifecycleState.Closed &&
                pendingEntries.Count == newlyPublishedBallots.Count)
            {
                await TryMarkTallyReadyWithPublishedSnapshotAsync(
                    repository,
                    election,
                    existingPublishedBallots.Concat(newlyPublishedBallots).OrderBy(x => x.PublicationSequence).ToArray(),
                    blockIndex.Value,
                    _blockchainCache.CurrentBlockId.Value);
            }

            await unitOfWork.CommitAsync();
        }
    }

    private async Task<PublicationPayload> PreparePublicationPayloadAsync(
        IElectionsRepository repository,
        ElectionId electionId,
        ElectionAcceptedBallotRecord acceptedBallot,
        DateTime observedAt,
        long blockHeight,
        Guid blockId)
    {
        var attempt = _publicationCryptoService.PrepareForPublication(
            acceptedBallot.EncryptedBallotPackage,
            acceptedBallot.ProofBundle);

        if (!attempt.IsSuccessful)
        {
            attempt = _publicationCryptoService.PrepareForPublication(
                acceptedBallot.EncryptedBallotPackage,
                acceptedBallot.ProofBundle);
        }

        if (attempt.IsSuccessful)
        {
            return new PublicationPayload(
                attempt.PublishedEncryptedBallotPackage!,
                attempt.PublishedProofBundle!);
        }

        await RegisterIssueAsync(
            repository,
            electionId,
            ElectionPublicationIssueCode.RerandomizationFallback,
            observedAt,
            blockHeight,
            blockId);

        _logger.LogWarning(
            "[ElectionBallotPublicationService] Publishing accepted ballot without rerandomization for election {ElectionId}: {FailureCode} {FailureReason}",
            electionId,
            attempt.FailureCode,
            attempt.FailureReason);

        return new PublicationPayload(
            acceptedBallot.EncryptedBallotPackage,
            acceptedBallot.ProofBundle);
    }

    private async Task TryMarkTallyReadyAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        long blockHeight,
        Guid blockId)
    {
        if (election.TallyReadyAt.HasValue)
        {
            return;
        }

        var pendingEntries = await repository.GetBallotMemPoolEntriesAsync(election.ElectionId);
        if (pendingEntries.Count > 0)
        {
            return;
        }

        var acceptedBallots = await repository.GetAcceptedBallotsAsync(election.ElectionId);
        var publishedBallots = await repository.GetPublishedBallotsAsync(election.ElectionId);
        await TryMarkTallyReadyWithPublishedSnapshotAsync(
            repository,
            election,
            publishedBallots,
            blockHeight,
            blockId,
            acceptedBallots);
    }

    private async Task TryMarkTallyReadyWithPublishedSnapshotAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        IReadOnlyList<ElectionPublishedBallotRecord> publishedBallots,
        long blockHeight,
        Guid blockId,
        IReadOnlyList<ElectionAcceptedBallotRecord>? acceptedBallots = null)
    {
        if (election.TallyReadyAt.HasValue)
        {
            return;
        }

        acceptedBallots ??= await repository.GetAcceptedBallotsAsync(election.ElectionId);
        if (acceptedBallots.Count != publishedBallots.Count)
        {
            _logger.LogWarning(
                "[ElectionBallotPublicationService] Tally-ready reconciliation failed for election {ElectionId}: accepted ballot count {AcceptedCount} does not match published ballot count {PublishedCount}.",
                election.ElectionId,
                acceptedBallots.Count,
                publishedBallots.Count);
            await RegisterIssueAsync(
                repository,
                election.ElectionId,
                ElectionPublicationIssueCode.ReplayMismatch,
                DateTime.UtcNow,
                blockHeight,
                blockId);
            return;
        }

        var replay = _publicationCryptoService.ReplayPublishedBallots(
            publishedBallots.Select(x => x.EncryptedBallotPackage).ToArray());
        if (!replay.IsSuccessful)
        {
            _logger.LogWarning(
                "[ElectionBallotPublicationService] Tally-ready replay failed for election {ElectionId}: {FailureCode} {FailureReason}",
                election.ElectionId,
                replay.FailureCode,
                replay.FailureReason);
            await RegisterIssueAsync(
                repository,
                election.ElectionId,
                ElectionPublicationIssueCode.UnsupportedBallotPayload,
                DateTime.UtcNow,
                blockHeight,
                blockId);
            return;
        }

        var recordedAt = DateTime.UtcNow;
        var acceptedHash = ComputeAcceptedBallotInventoryHash(acceptedBallots);
        var publishedHash = ComputePublishedBallotStreamHash(publishedBallots);
        var artifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.TallyReady,
            election,
            election.OwnerPublicAddress,
            recordedAt: recordedAt,
            acceptedBallotCount: acceptedBallots.Count,
            acceptedBallotSetHash: acceptedHash,
            publishedBallotCount: publishedBallots.Count,
            publishedBallotStreamHash: publishedHash,
            finalEncryptedTallyHash: replay.FinalEncryptedTallyHash,
            sourceBlockHeight: blockHeight,
            sourceBlockId: blockId);

        var updatedElection = election with
        {
            LastUpdatedAt = recordedAt,
            TallyReadyAt = recordedAt,
            TallyReadyArtifactId = artifact.Id,
        };

        await repository.SaveBoundaryArtifactAsync(artifact);
        await repository.SaveElectionAsync(updatedElection);
        _logger.LogInformation(
            "[ElectionBallotPublicationService] Election {ElectionId} reached tally_ready with {AcceptedCount} accepted ballot(s) and {PublishedCount} published ballot(s).",
            election.ElectionId,
            acceptedBallots.Count,
            publishedBallots.Count);
    }

    private async Task RegisterIssueAsync(
        IElectionsRepository repository,
        ElectionId electionId,
        ElectionPublicationIssueCode issueCode,
        DateTime observedAt,
        long blockHeight,
        Guid blockId)
    {
        var existingIssue = await repository.GetPublicationIssueAsync(electionId, issueCode);
        if (existingIssue is null)
        {
            await repository.SavePublicationIssueAsync(
                ElectionModelFactory.CreatePublicationIssue(
                    electionId,
                    issueCode,
                    observedAt,
                    blockHeight,
                    blockId));
            return;
        }

        await repository.UpdatePublicationIssueAsync(existingIssue.RegisterOccurrence(
            observedAt,
            blockHeight,
            blockId));
    }

    private int ResolvePublishCount(ElectionLifecycleState lifecycleState, int pendingCount)
    {
        if (lifecycleState == ElectionLifecycleState.Closed)
        {
            return pendingCount;
        }

        if (lifecycleState != ElectionLifecycleState.Open || pendingCount < _options.HighWaterMark)
        {
            return 0;
        }

        var targetReduction = pendingCount - _options.LowWaterMark;
        return Math.Max(0, Math.Min(_options.MaxBatchPerBlock, targetReduction));
    }

    private static IReadOnlyList<ElectionBallotMemPoolRecord> SelectEntriesForPublication(
        IReadOnlyList<ElectionBallotMemPoolRecord> entries,
        int publishCount)
    {
        var working = entries.ToList();
        var selected = new List<ElectionBallotMemPoolRecord>(publishCount);
        for (var index = 0; index < publishCount && working.Count > 0; index++)
        {
            var randomIndex = Random.Shared.Next(working.Count);
            selected.Add(working[randomIndex]);
            working.RemoveAt(randomIndex);
        }

        return selected;
    }

    private static byte[] ComputeAcceptedBallotInventoryHash(
        IReadOnlyList<ElectionAcceptedBallotRecord> acceptedBallots)
    {
        var payload = string.Join(
            '\n',
            acceptedBallots
                .OrderBy(x => x.BallotNullifier, StringComparer.Ordinal)
                .Select(x => $"{x.BallotNullifier}|{ComputeHexSha256(x.EncryptedBallotPackage)}|{ComputeHexSha256(x.ProofBundle)}"));

        return SHA256.HashData(Encoding.UTF8.GetBytes(payload));
    }

    private static byte[] ComputePublishedBallotStreamHash(
        IReadOnlyList<ElectionPublishedBallotRecord> publishedBallots)
    {
        var payload = string.Join(
            '\n',
            publishedBallots
                .OrderBy(x => x.PublicationSequence)
                .Select(x => $"{x.PublicationSequence}|{ComputeHexSha256(x.EncryptedBallotPackage)}|{ComputeHexSha256(x.ProofBundle)}"));

        return SHA256.HashData(Encoding.UTF8.GetBytes(payload));
    }

    private static string ComputeHexSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));

    private sealed record PublicationPayload(
        string EncryptedBallotPackage,
        string ProofBundle);
}
