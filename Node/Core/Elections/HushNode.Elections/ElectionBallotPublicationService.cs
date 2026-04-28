using System.Data;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HushNode.Caching;
using HushNode.Credentials;
using HushNode.Reactions.Crypto;
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
    ICredentialsProvider credentialsProvider,
    ElectionBallotPublicationOptions options,
    ILogger<ElectionBallotPublicationService> logger,
    IElectionResultCryptoService? electionResultCryptoService = null,
    ICloseCountingExecutorKeyRegistry? closeCountingExecutorKeyRegistry = null,
    ICloseCountingExecutorEnvelopeCrypto? closeCountingExecutorEnvelopeCrypto = null,
    IAdminOnlyProtectedTallyEnvelopeCrypto? adminOnlyProtectedTallyEnvelopeCrypto = null,
    IBabyJubJub? curve = null) :
    IElectionBallotPublicationService,
    IHandleAsync<BlockIndexCompletedEvent>
{
    private const string ExecutorSessionKeyAlgorithm = "ecies-secp256k1-v1";
    private static readonly JsonSerializerOptions ResultPayloadJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions DevPublishedPayloadJsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    private readonly IUnitOfWorkProvider<ElectionsDbContext> _unitOfWorkProvider = unitOfWorkProvider;
    private readonly IElectionBallotPublicationCryptoService _publicationCryptoService = publicationCryptoService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly ICredentialsProvider _credentialsProvider = credentialsProvider;
    private readonly ElectionBallotPublicationOptions _options = options;
    private readonly ILogger<ElectionBallotPublicationService> _logger = logger;
    private readonly IElectionResultCryptoService? _electionResultCryptoService = electionResultCryptoService;
    private readonly IBabyJubJub _curve = curve ?? new BabyJubJubCurve();
    private readonly ICloseCountingExecutorKeyRegistry _closeCountingExecutorKeyRegistry =
        closeCountingExecutorKeyRegistry ?? new InMemoryCloseCountingExecutorKeyRegistry();
    private readonly ICloseCountingExecutorEnvelopeCrypto _closeCountingExecutorEnvelopeCrypto =
        closeCountingExecutorEnvelopeCrypto ?? new UnavailableCloseCountingExecutorEnvelopeCrypto();
    private readonly IAdminOnlyProtectedTallyEnvelopeCrypto _adminOnlyProtectedTallyEnvelopeCrypto =
        adminOnlyProtectedTallyEnvelopeCrypto ?? new UnavailableAdminOnlyProtectedTallyEnvelopeCrypto();

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
                    await TryAdvanceClosedElectionAsync(repository, election, blockIndex.Value, _blockchainCache.CurrentBlockId.Value);
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
            var selectedAcceptedBallots = new List<(ElectionBallotMemPoolRecord Entry, ElectionAcceptedBallotRecord AcceptedBallot)>(selectedEntries.Count);

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

                selectedAcceptedBallots.Add((entry, acceptedBallot));
            }

            foreach (var selection in ReorderSelectionsForPrivacy(selectedAcceptedBallots))
            {
                var entry = selection.Entry;
                var acceptedBallot = selection.AcceptedBallot;

                var publicationPayload = await PreparePublicationPayloadAsync(
                    repository,
                    election,
                    electionId,
                    acceptedBallot,
                    publishedAt,
                    blockIndex.Value,
                    _blockchainCache.CurrentBlockId.Value);
                if (publicationPayload is null)
                {
                    continue;
                }

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
                await TryAdvanceClosedElectionWithPublishedSnapshotAsync(
                    repository,
                    election,
                    existingPublishedBallots.Concat(newlyPublishedBallots).OrderBy(x => x.PublicationSequence).ToArray(),
                    blockIndex.Value,
                    _blockchainCache.CurrentBlockId.Value);
            }

            await unitOfWork.CommitAsync();
        }
    }

    public async Task RepairClosedElectionResultsAsync(ElectionId electionId)
    {
        _options.Validate();

        var repairBlockIndex = _blockchainCache.LastBlockIndex.Value >= 0
            ? _blockchainCache.LastBlockIndex
            : new BlockIndex(0);
        await ProcessPendingPublicationAsync(repairBlockIndex);

        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionForUpdateAsync(electionId);
        if (election is null || election.LifecycleState != ElectionLifecycleState.Closed)
        {
            return;
        }

        var unofficialResult = election.UnofficialResultArtifactId.HasValue
            ? await repository.GetResultArtifactAsync(election.UnofficialResultArtifactId.Value)
            : await repository.GetResultArtifactAsync(election.ElectionId, ElectionResultArtifactKind.Unofficial);
        if (unofficialResult is not null)
        {
            return;
        }

        var pendingEntries = await repository.GetBallotMemPoolEntriesAsync(electionId);
        if (pendingEntries.Count > 0)
        {
            return;
        }

        var acceptedBallots = await repository.GetAcceptedBallotsAsync(electionId);
        var publishedBallots = await repository.GetPublishedBallotsAsync(electionId);
        var repaired = await TryCreateAdminOnlyClosedElectionArtifactsAsync(
            repository,
            election,
            acceptedBallots,
            publishedBallots,
            repairBlockIndex.Value,
            _blockchainCache.CurrentBlockId.Value);
        if (repaired)
        {
            await unitOfWork.CommitAsync();
        }
    }

    private async Task<PublicationPayload?> PreparePublicationPayloadAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        ElectionId electionId,
        ElectionAcceptedBallotRecord acceptedBallot,
        DateTime observedAt,
        long blockHeight,
        Guid blockId)
    {
        var devModeBallot = TryParseDevModeAcceptedBallotPackage(acceptedBallot);
        if (devModeBallot is not null)
        {
            if (election.BindingStatus != ElectionBindingStatus.NonBinding)
            {
                await RegisterIssueAsync(
                    repository,
                    electionId,
                    ElectionPublicationIssueCode.UnsupportedBallotPayload,
                    observedAt,
                    blockHeight,
                    blockId);
                _logger.LogWarning(
                    "[ElectionBallotPublicationService] Rejecting dev/open ballot publication fallback for binding election {ElectionId}.",
                    electionId);
                return null;
            }

            return BuildDevModePublicationPayload(devModeBallot);
        }

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

    private static AdminOnlyDevModeBallotPackage? TryParseDevModeAcceptedBallotPackage(ElectionAcceptedBallotRecord acceptedBallot)
    {
        AdminOnlyDevModeBallotPackage? payload;
        try
        {
            payload = JsonSerializer.Deserialize<AdminOnlyDevModeBallotPackage>(
                acceptedBallot.EncryptedBallotPackage,
                ResultPayloadJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (payload is null ||
            !string.Equals(payload.Mode, "election-dev-mode-v1", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(payload.PackageType, "dev-protected-ballot", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(payload.ElectionId) ||
            string.IsNullOrWhiteSpace(payload.OptionId) ||
            string.IsNullOrWhiteSpace(payload.OptionLabel))
        {
            return null;
        }

        return payload;
    }

    private static PublicationPayload BuildDevModePublicationPayload(AdminOnlyDevModeBallotPackage payload)
    {
        var publishedPackage = JsonSerializer.Serialize(
            payload with
            {
                PackageType = "dev-published-ballot",
                ActorPublicAddress = null,
                SelectionFingerprint = null,
                GeneratedAt = null,
                PublicationNonce = Guid.NewGuid().ToString("N"),
            },
            DevPublishedPayloadJsonOptions);
        var publishedProofBundle = JsonSerializer.Serialize(
            new PublishedDevModeProofBundle(
                Mode: "election-dev-mode-v1",
                ProofType: "dev-publication-proof",
                PublicationVariant: "plaintext-choice-projection",
                PublishedBallotHash: ComputeHexSha256(publishedPackage)),
            DevPublishedPayloadJsonOptions);

        return new PublicationPayload(publishedPackage, publishedProofBundle);
    }

    private async Task TryAdvanceClosedElectionAsync(
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
        await TryAdvanceClosedElectionWithPublishedSnapshotAsync(
            repository,
            election,
            publishedBallots,
            blockHeight,
            blockId,
            acceptedBallots);
    }

    private async Task TryAdvanceClosedElectionWithPublishedSnapshotAsync(
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

        var acceptedHash = ComputeAcceptedBallotInventoryHash(acceptedBallots);
        var publishedHash = ComputePublishedBallotStreamHash(publishedBallots);
        var boundaryArtifacts = await repository.GetBoundaryArtifactsAsync(election.ElectionId);
        var openArtifact = boundaryArtifacts.FirstOrDefault(x =>
            x.Id == election.OpenArtifactId &&
            x.ArtifactType == ElectionBoundaryArtifactType.Open);
        var ceremonySnapshot = ElectionProtectedTallyBinding.ResolveOpenBoundaryBinding(election, openArtifact);
        var replay = _publicationCryptoService.ReplayPublishedBallots(
            publishedBallots.Select(x => x.EncryptedBallotPackage).ToArray());
        var resolvedFinalEncryptedTallyHash = replay.FinalEncryptedTallyHash;
        if (!replay.IsSuccessful)
        {
            var repaired = await TryCreateAdminOnlyClosedElectionArtifactsAsync(
                repository,
                election,
                acceptedBallots,
                publishedBallots,
                blockHeight,
                blockId,
                boundaryArtifacts,
                acceptedHash,
                publishedHash,
                ceremonySnapshot);
            if (repaired)
            {
                _logger.LogInformation(
                    "[ElectionBallotPublicationService] Election {ElectionId} used the admin-only dev ballot recovery path to publish tally_ready and unofficial results.",
                    election.ElectionId);
                return;
            }

            if (election.GovernanceMode == ElectionGovernanceMode.TrusteeThreshold &&
                ElectionDevModePublishedBallotSupport.TryBuildPublishedBallotTally(
                    election,
                    publishedBallots,
                    out var devModeTally) &&
                devModeTally is not null)
            {
                resolvedFinalEncryptedTallyHash = devModeTally.FinalEncryptedTallyHash;
                _logger.LogInformation(
                    "[ElectionBallotPublicationService] Election {ElectionId} used the trustee-threshold dev ballot fallback to bind close-counting progress to the published ballot inventory.",
                    election.ElectionId);
            }
            else
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
        }

        if (election.GovernanceMode != ElectionGovernanceMode.TrusteeThreshold)
        {
            var recordedAt = DateTime.UtcNow;
            var artifact = ElectionModelFactory.CreateBoundaryArtifact(
                ElectionBoundaryArtifactType.TallyReady,
                election,
                election.OwnerPublicAddress,
                ceremonySnapshot: ceremonySnapshot,
                recordedAt: recordedAt,
                acceptedBallotCount: acceptedBallots.Count,
                acceptedBallotSetHash: acceptedHash,
                publishedBallotCount: publishedBallots.Count,
                publishedBallotStreamHash: publishedHash,
                finalEncryptedTallyHash: resolvedFinalEncryptedTallyHash,
                sourceBlockHeight: blockHeight,
                sourceBlockId: blockId);

            ElectionResultArtifactRecord? unofficialResult;
            if (acceptedBallots.Count == 0)
            {
                unofficialResult = await TryCreateZeroBallotUnofficialResultAsync(
                    repository,
                    election,
                    acceptedBallots,
                    artifact,
                    recordedAt,
                    blockHeight,
                    blockId);
            }
            else
            {
                unofficialResult = await TryCreateAdminOnlyUnofficialResultAsync(
                    repository,
                    election,
                    artifact,
                    publishedBallots,
                    recordedAt,
                    blockHeight,
                    blockId,
                    ceremonySnapshot);
            }

            var updatedElection = election with
            {
                LastUpdatedAt = recordedAt,
                TallyReadyAt = recordedAt,
                TallyReadyArtifactId = artifact.Id,
                UnofficialResultArtifactId = unofficialResult?.Id,
                ClosedProgressStatus = unofficialResult is null
                    ? election.ClosedProgressStatus == ElectionClosedProgressStatus.None
                        ? ElectionClosedProgressStatus.TallyCalculationInProgress
                        : election.ClosedProgressStatus
                    : ElectionClosedProgressStatus.None,
            };

            await repository.SaveBoundaryArtifactAsync(artifact);
            if (unofficialResult is not null)
            {
                await repository.SaveResultArtifactAsync(unofficialResult);
            }

            await repository.SaveElectionAsync(updatedElection);
            _logger.LogInformation(
                "[ElectionBallotPublicationService] Election {ElectionId} reached tally_ready with {AcceptedCount} accepted ballot(s) and {PublishedCount} published ballot(s).",
                election.ElectionId,
                acceptedBallots.Count,
                publishedBallots.Count);
            return;
        }

        var activeSession = await repository.GetActiveFinalizationSessionAsync(election.ElectionId);
        if (activeSession is not null)
        {
            if (election.ClosedProgressStatus != ElectionClosedProgressStatus.WaitingForTrusteeShares)
            {
                await repository.SaveElectionAsync(election with
                {
                    LastUpdatedAt = DateTime.UtcNow,
                    ClosedProgressStatus = ElectionClosedProgressStatus.WaitingForTrusteeShares,
                });
            }

            return;
        }

        var closeArtifact = boundaryArtifacts.FirstOrDefault(x =>
            x.Id == election.CloseArtifactId &&
            x.ArtifactType == ElectionBoundaryArtifactType.Close);
        if (ceremonySnapshot is null || closeArtifact is null)
        {
            _logger.LogWarning(
                "[ElectionBallotPublicationService] Close-counting session could not be created for election {ElectionId} because the required open/close artifacts were not available.",
                election.ElectionId);
            return;
        }

        var createdAt = DateTime.UtcNow;
        var session = ElectionModelFactory.CreateFinalizationSession(
            election,
            closeArtifact.Id,
            acceptedHash,
            resolvedFinalEncryptedTallyHash!,
            ElectionFinalizationSessionPurpose.CloseCounting,
            ceremonySnapshot,
            ceremonySnapshot.RequiredApprovalCount,
            ceremonySnapshot.ActiveTrustees.ToArray(),
            election.OwnerPublicAddress,
            governedProposalId: null,
            createdAt: createdAt,
            latestBlockHeight: blockHeight,
            latestBlockId: blockId);
        var closeCountingJob = ElectionModelFactory.CreateCloseCountingJob(
            session,
            createdAt: createdAt,
            latestBlockHeight: blockHeight,
            latestBlockId: blockId);
        if (!_closeCountingExecutorEnvelopeCrypto.IsAvailable(out var closeCountingEnvelopeError))
        {
            _logger.LogError(
                "[ElectionBallotPublicationService] Close-counting session could not be created for election {ElectionId} because trustee executor custody is unavailable: {Error}",
                election.ElectionId,
                closeCountingEnvelopeError);
            return;
        }

        var executorSessionKeys = _closeCountingExecutorKeyRegistry.Create(closeCountingJob.Id, ExecutorSessionKeyAlgorithm);
        var executorSessionKeyEnvelope = ElectionModelFactory.CreateExecutorSessionKeyEnvelope(
            closeCountingJob.Id,
            executorSessionKeys.PublicKey,
            _closeCountingExecutorEnvelopeCrypto.SealPrivateKey(
                executorSessionKeys.PrivateKey,
                closeCountingJob.Id,
                ExecutorSessionKeyAlgorithm),
            ExecutorSessionKeyAlgorithm,
            _closeCountingExecutorEnvelopeCrypto.SealAlgorithm,
            createdAt: createdAt,
            sealedByServiceIdentity: _closeCountingExecutorEnvelopeCrypto.SealedByServiceIdentity);

        await repository.SaveFinalizationSessionAsync(session);
        await repository.SaveCloseCountingJobAsync(closeCountingJob);
        await repository.SaveExecutorSessionKeyEnvelopeAsync(executorSessionKeyEnvelope);
        await repository.SaveElectionAsync(election with
        {
            LastUpdatedAt = createdAt,
            ClosedProgressStatus = ElectionClosedProgressStatus.WaitingForTrusteeShares,
        });

        _logger.LogInformation(
            "[ElectionBallotPublicationService] Election {ElectionId} drained BallotMemPool and created close-counting session {SessionId} for {AcceptedCount} accepted ballot(s).",
            election.ElectionId,
            session.Id,
            acceptedBallots.Count);
    }

    private async Task<bool> TryCreateAdminOnlyClosedElectionArtifactsAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        IReadOnlyList<ElectionAcceptedBallotRecord> acceptedBallots,
        IReadOnlyList<ElectionPublishedBallotRecord> publishedBallots,
        long? blockHeight,
        Guid? blockId,
        IReadOnlyList<ElectionBoundaryArtifactRecord>? boundaryArtifacts = null,
        byte[]? acceptedHash = null,
        byte[]? publishedHash = null,
        ElectionCeremonyBindingSnapshot? ceremonySnapshot = null)
    {
        if (election.GovernanceMode != ElectionGovernanceMode.AdminOnly)
        {
            return false;
        }

        boundaryArtifacts ??= await repository.GetBoundaryArtifactsAsync(election.ElectionId);
        acceptedHash ??= ComputeAcceptedBallotInventoryHash(acceptedBallots);
        publishedHash ??= ComputePublishedBallotStreamHash(publishedBallots);

        var tallyReadyArtifact = FindTallyReadyArtifact(election, boundaryArtifacts);
        var repairRecordedAt = DateTime.UtcNow;

        if (acceptedBallots.Count == 0)
        {
            if (publishedBallots.Count != 0)
            {
                return false;
            }

            if (tallyReadyArtifact is null)
            {
                var emptyReplay = _publicationCryptoService.ReplayPublishedBallots(Array.Empty<string>());
                if (!emptyReplay.IsSuccessful || emptyReplay.FinalEncryptedTallyHash is not { Length: > 0 })
                {
                    return false;
                }

                ceremonySnapshot ??= ElectionProtectedTallyBinding.ResolveOpenBoundaryBinding(
                    election,
                    boundaryArtifacts.FirstOrDefault(x =>
                        x.Id == election.OpenArtifactId &&
                        x.ArtifactType == ElectionBoundaryArtifactType.Open));
                tallyReadyArtifact = ElectionModelFactory.CreateBoundaryArtifact(
                    ElectionBoundaryArtifactType.TallyReady,
                    election,
                    election.OwnerPublicAddress,
                    ceremonySnapshot: ceremonySnapshot,
                    recordedAt: repairRecordedAt,
                    acceptedBallotCount: 0,
                    acceptedBallotSetHash: acceptedHash,
                    publishedBallotCount: 0,
                    publishedBallotStreamHash: publishedHash,
                    finalEncryptedTallyHash: emptyReplay.FinalEncryptedTallyHash,
                    sourceBlockHeight: blockHeight,
                    sourceBlockId: blockId);
                await repository.SaveBoundaryArtifactAsync(tallyReadyArtifact);
            }

            var zeroUnofficialResult = await TryCreateZeroBallotUnofficialResultAsync(
                repository,
                election,
                acceptedBallots,
                tallyReadyArtifact,
                repairRecordedAt,
                blockHeight,
                blockId);
            if (zeroUnofficialResult is null)
            {
                return false;
            }

            await repository.SaveResultArtifactAsync(zeroUnofficialResult);
            await repository.SaveElectionAsync(election with
            {
                LastUpdatedAt = repairRecordedAt,
                TallyReadyAt = election.TallyReadyAt ?? tallyReadyArtifact.RecordedAt,
                TallyReadyArtifactId = tallyReadyArtifact.Id,
                UnofficialResultArtifactId = zeroUnofficialResult.Id,
                ClosedProgressStatus = ElectionClosedProgressStatus.None,
            });
            return true;
        }

        if (acceptedBallots.Count != publishedBallots.Count || publishedBallots.Count == 0)
        {
            return false;
        }

        if (!TryBuildAdminOnlyDevModeResult(election, publishedBallots, out var devModeResult) ||
            devModeResult is null)
        {
            return false;
        }

        var closeSnapshot = await repository.GetEligibilitySnapshotAsync(
            election.ElectionId,
            ElectionEligibilitySnapshotType.Close);
        if (closeSnapshot is null)
        {
            _logger.LogWarning(
                "[ElectionBallotPublicationService] Admin-only dev result recovery could not run for election {ElectionId} because the close eligibility snapshot is unavailable.",
                election.ElectionId);
            return false;
        }

        if (!DoesCloseSnapshotReconcileWithAdminOnlyDevTurnout(closeSnapshot, devModeResult))
        {
            _logger.LogWarning(
                "[ElectionBallotPublicationService] Admin-only dev result recovery could not run for election {ElectionId} because the close eligibility snapshot turnout totals do not reconcile with the published ballots.",
                election.ElectionId);
            return false;
        }

        if (tallyReadyArtifact is not null)
        {
            if ((tallyReadyArtifact.AcceptedBallotCount.HasValue &&
                 tallyReadyArtifact.AcceptedBallotCount.Value != acceptedBallots.Count) ||
                (tallyReadyArtifact.PublishedBallotCount.HasValue &&
                 tallyReadyArtifact.PublishedBallotCount.Value != publishedBallots.Count) ||
                (tallyReadyArtifact.AcceptedBallotSetHash is { Length: > 0 } &&
                 !ByteArrayEquals(tallyReadyArtifact.AcceptedBallotSetHash, acceptedHash)) ||
                (tallyReadyArtifact.PublishedBallotStreamHash is { Length: > 0 } &&
                 !ByteArrayEquals(tallyReadyArtifact.PublishedBallotStreamHash, publishedHash)) ||
                (tallyReadyArtifact.FinalEncryptedTallyHash is { Length: > 0 } &&
                 !ByteArrayEquals(tallyReadyArtifact.FinalEncryptedTallyHash, devModeResult.FinalEncryptedTallyHash)))
            {
                _logger.LogWarning(
                    "[ElectionBallotPublicationService] Admin-only dev result recovery could not run for election {ElectionId} because the existing tally-ready artifact does not match the current published ballot inventory.",
                    election.ElectionId);
                return false;
            }
        }
        else
        {
            ceremonySnapshot ??= ElectionProtectedTallyBinding.ResolveOpenBoundaryBinding(
                election,
                boundaryArtifacts.FirstOrDefault(x =>
                    x.Id == election.OpenArtifactId &&
                    x.ArtifactType == ElectionBoundaryArtifactType.Open));
            tallyReadyArtifact = ElectionModelFactory.CreateBoundaryArtifact(
                ElectionBoundaryArtifactType.TallyReady,
                election,
                election.OwnerPublicAddress,
                ceremonySnapshot: ceremonySnapshot,
                recordedAt: repairRecordedAt,
                acceptedBallotCount: acceptedBallots.Count,
                acceptedBallotSetHash: acceptedHash,
                publishedBallotCount: publishedBallots.Count,
                publishedBallotStreamHash: publishedHash,
                finalEncryptedTallyHash: devModeResult.FinalEncryptedTallyHash,
                sourceBlockHeight: blockHeight,
                sourceBlockId: blockId);
            await repository.SaveBoundaryArtifactAsync(tallyReadyArtifact);
        }

        var unofficialResult = await TryCreateParticipantEncryptedUnofficialResultAsync(
            repository,
            election,
            tallyReadyArtifact,
            devModeResult.NamedOptionResults,
            devModeResult.BlankCount,
            devModeResult.TotalVotedCount,
            closeSnapshot,
            repairRecordedAt,
            blockHeight,
            blockId);
        if (unofficialResult is null)
        {
            return false;
        }

        await repository.SaveResultArtifactAsync(unofficialResult);
        await repository.SaveElectionAsync(election with
        {
            LastUpdatedAt = repairRecordedAt,
            TallyReadyAt = election.TallyReadyAt ?? tallyReadyArtifact.RecordedAt,
            TallyReadyArtifactId = tallyReadyArtifact.Id,
            UnofficialResultArtifactId = unofficialResult.Id,
            ClosedProgressStatus = ElectionClosedProgressStatus.None,
        });
        return true;
    }

    private async Task<ElectionResultArtifactRecord?> TryCreateAdminOnlyUnofficialResultAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        ElectionBoundaryArtifactRecord tallyReadyArtifact,
        IReadOnlyList<ElectionPublishedBallotRecord> publishedBallots,
        DateTime recordedAt,
        long? blockHeight,
        Guid? blockId,
        ElectionCeremonyBindingSnapshot? ceremonySnapshot = null)
    {
        return election.SelectedProfileDevOnly
            ? await TryCreateAdminOnlyDevModeUnofficialResultAsync(
                repository,
                election,
                tallyReadyArtifact,
                publishedBallots,
                recordedAt,
                blockHeight,
                blockId)
            : await TryCreateAdminOnlyProtectedUnofficialResultAsync(
                repository,
                election,
                tallyReadyArtifact,
                publishedBallots,
                recordedAt,
                blockHeight,
                blockId,
                ceremonySnapshot);
    }

    private async Task<ElectionResultArtifactRecord?> TryCreateAdminOnlyDevModeUnofficialResultAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        ElectionBoundaryArtifactRecord tallyReadyArtifact,
        IReadOnlyList<ElectionPublishedBallotRecord> publishedBallots,
        DateTime recordedAt,
        long? blockHeight,
        Guid? blockId)
    {
        if (!TryBuildAdminOnlyDevModeResult(election, publishedBallots, out var devModeResult) ||
            devModeResult is null)
        {
            return null;
        }

        var closeSnapshot = await repository.GetEligibilitySnapshotAsync(
            election.ElectionId,
            ElectionEligibilitySnapshotType.Close);
        if (closeSnapshot is null)
        {
            _logger.LogWarning(
                "[ElectionBallotPublicationService] Admin-only close result publication could not run for election {ElectionId} because the close eligibility snapshot is unavailable.",
                election.ElectionId);
            return null;
        }

        if (!DoesCloseSnapshotReconcileWithAdminOnlyDevTurnout(closeSnapshot, devModeResult))
        {
            _logger.LogWarning(
                "[ElectionBallotPublicationService] Admin-only close result publication could not run for election {ElectionId} because the close eligibility snapshot turnout totals do not reconcile with the published ballots.",
                election.ElectionId);
            return null;
        }

        return await TryCreateParticipantEncryptedUnofficialResultAsync(
            repository,
            election,
            tallyReadyArtifact,
            devModeResult.NamedOptionResults,
            devModeResult.BlankCount,
            devModeResult.TotalVotedCount,
            closeSnapshot,
            recordedAt,
            blockHeight,
            blockId);
    }

    private async Task<ElectionResultArtifactRecord?> TryCreateAdminOnlyProtectedUnofficialResultAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        ElectionBoundaryArtifactRecord tallyReadyArtifact,
        IReadOnlyList<ElectionPublishedBallotRecord> publishedBallots,
        DateTime recordedAt,
        long? blockHeight,
        Guid? blockId,
        ElectionCeremonyBindingSnapshot? ceremonySnapshot)
    {
        if (_electionResultCryptoService is null)
        {
            _logger.LogWarning(
                "[ElectionBallotPublicationService] Admin-only protected result publication could not run for election {ElectionId} because the result crypto service is unavailable.",
                election.ElectionId);
            return null;
        }

        if (publishedBallots.Count == 0)
        {
            return null;
        }

        var releaseShareResult = await ResolveAdminOnlyProtectedReleaseShareAsync(
                repository,
                election,
                tallyReadyArtifact,
                ceremonySnapshot);
        if (!releaseShareResult.IsSuccess)
        {
            _logger.LogWarning(
                "[ElectionBallotPublicationService] Admin-only protected result publication could not run for election {ElectionId}: {FailureReason}",
                election.ElectionId,
                releaseShareResult.Error);
            return null;
        }

        var closeSnapshot = await repository.GetEligibilitySnapshotAsync(
            election.ElectionId,
            ElectionEligibilitySnapshotType.Close);
        if (closeSnapshot is null)
        {
            _logger.LogWarning(
                "[ElectionBallotPublicationService] Admin-only protected result publication could not run for election {ElectionId} because the close eligibility snapshot is unavailable.",
                election.ElectionId);
            return null;
        }

        var release = _electionResultCryptoService.TryReleaseAggregateTally(
            publishedBallots.Select(x => x.EncryptedBallotPackage).ToArray(),
            [releaseShareResult.ReleaseShare!],
            closeSnapshot.ActiveDenominatorCount);
        if (!release.IsSuccessful)
        {
            _logger.LogWarning(
                "[ElectionBallotPublicationService] Admin-only protected result publication could not run for election {ElectionId}: {FailureCode} {FailureReason}",
                election.ElectionId,
                release.FailureCode,
                release.FailureReason);
            return null;
        }

        if (release.FinalEncryptedTallyHash is null ||
            !ByteArrayEquals(release.FinalEncryptedTallyHash, tallyReadyArtifact.FinalEncryptedTallyHash))
        {
            _logger.LogWarning(
                "[ElectionBallotPublicationService] Admin-only protected result publication could not run for election {ElectionId} because the released tally hash does not match tally_ready.",
                election.ElectionId);
            return null;
        }

        var decodedCounts = release.DecodedCounts ?? Array.Empty<int>();
        if (publishedBallots.Count == 0 && decodedCounts.Count == 0)
        {
            decodedCounts = Enumerable.Repeat(0, election.Options.Count).ToArray();
        }

        if (decodedCounts.Count != election.Options.Count)
        {
            _logger.LogWarning(
                "[ElectionBallotPublicationService] Admin-only protected result publication could not run for election {ElectionId} because the released tally slot count does not match the ballot options.",
                election.ElectionId);
            return null;
        }

        var optionCounts = election.Options
            .Select((option, index) => new { Option = option, Count = decodedCounts[index] })
            .ToArray();
        var blankOption = optionCounts.FirstOrDefault(x => x.Option.IsBlankOption);
        var namedOptionResults = optionCounts
            .Where(x => !x.Option.IsBlankOption)
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Option.BallotOrder)
            .Select((x, index) => new ElectionResultOptionCount(
                x.Option.OptionId,
                x.Option.DisplayLabel,
                x.Option.ShortDescription,
                x.Option.BallotOrder,
                index + 1,
                x.Count))
            .ToArray();
        var blankCount = blankOption?.Count ?? 0;
        var totalVotedCount = decodedCounts.Sum();

        if (totalVotedCount != closeSnapshot.CountedParticipationCount)
        {
            _logger.LogWarning(
                "[ElectionBallotPublicationService] Admin-only protected result publication could not run for election {ElectionId} because the released tally does not reconcile with the close snapshot.",
                election.ElectionId);
            return null;
        }

        return await TryCreateParticipantEncryptedUnofficialResultAsync(
            repository,
            election,
            tallyReadyArtifact,
            namedOptionResults,
            blankCount,
            totalVotedCount,
            closeSnapshot,
            recordedAt,
            blockHeight,
            blockId);
    }

    private async Task<ElectionResultArtifactRecord?> TryCreateParticipantEncryptedUnofficialResultAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        ElectionBoundaryArtifactRecord tallyReadyArtifact,
        IReadOnlyList<ElectionResultOptionCount> namedOptionResults,
        int blankCount,
        int totalVotedCount,
        ElectionEligibilitySnapshotRecord closeSnapshot,
        DateTime recordedAt,
        long? blockHeight,
        Guid? blockId)
    {
        if (_electionResultCryptoService is null)
        {
            _logger.LogWarning(
                "[ElectionBallotPublicationService] Participant-encrypted unofficial result publication could not run for election {ElectionId} because the result crypto service is unavailable.",
                election.ElectionId);
            return null;
        }

        if (totalVotedCount != namedOptionResults.Sum(x => x.VoteCount) + blankCount)
        {
            _logger.LogWarning(
                "[ElectionBallotPublicationService] Participant-encrypted unofficial result publication could not run for election {ElectionId} because the result counts do not reconcile.",
                election.ElectionId);
            return null;
        }

        var didNotVoteCount = closeSnapshot.ActiveDenominatorCount - totalVotedCount;
        if (didNotVoteCount < 0)
        {
            _logger.LogWarning(
                "[ElectionBallotPublicationService] Participant-encrypted unofficial result publication could not run for election {ElectionId} because the denominator evidence is invalid.",
                election.ElectionId);
            return null;
        }

        var ownerAccess = await repository.GetElectionEnvelopeAccessAsync(election.ElectionId, election.OwnerPublicAddress);
        if (ownerAccess is null)
        {
            _logger.LogWarning(
                "[ElectionBallotPublicationService] Participant-encrypted unofficial result publication could not run for election {ElectionId} because the owner election envelope access record is unavailable.",
                election.ElectionId);
            return null;
        }

        var denominatorEvidence = new ElectionResultDenominatorEvidence(
            closeSnapshot.SnapshotType,
            closeSnapshot.Id,
            closeSnapshot.BoundaryArtifactId,
            closeSnapshot.ActiveDenominatorSetHash);
        var payload = SerializeResultArtifactPayload(
            election.Title,
            namedOptionResults,
            blankCount,
            totalVotedCount,
            closeSnapshot.ActiveDenominatorCount,
            didNotVoteCount,
            denominatorEvidence);
        var encryptedPayload = _electionResultCryptoService.EncryptForElectionParticipants(
            payload,
            ownerAccess.NodeEncryptedElectionPrivateKey);

        return ElectionModelFactory.CreateResultArtifact(
            election.ElectionId,
            ElectionResultArtifactKind.Unofficial,
            ElectionResultArtifactVisibility.ParticipantEncrypted,
            election.Title,
            namedOptionResults,
            blankCount,
            totalVotedCount,
            closeSnapshot.ActiveDenominatorCount,
            didNotVoteCount,
            denominatorEvidence,
            election.OwnerPublicAddress,
            tallyReadyArtifactId: tallyReadyArtifact.Id,
            encryptedPayload: encryptedPayload,
            recordedAt: recordedAt,
            sourceBlockHeight: blockHeight,
            sourceBlockId: blockId);
    }

    private async Task<ElectionResultArtifactRecord?> TryCreateZeroBallotUnofficialResultAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        IReadOnlyList<ElectionAcceptedBallotRecord> acceptedBallots,
        ElectionBoundaryArtifactRecord tallyReadyArtifact,
        DateTime recordedAt,
        long? blockHeight,
        Guid? blockId)
    {
        if (acceptedBallots.Count > 0)
        {
            return null;
        }

        if (_electionResultCryptoService is null)
        {
            _logger.LogWarning(
                "[ElectionBallotPublicationService] Zero-ballot unofficial result could not be published for election {ElectionId} because the result crypto service is unavailable.",
                election.ElectionId);
            return null;
        }

        var closeSnapshot = await repository.GetEligibilitySnapshotAsync(
            election.ElectionId,
            ElectionEligibilitySnapshotType.Close);
        if (closeSnapshot is null)
        {
            _logger.LogWarning(
                "[ElectionBallotPublicationService] Zero-ballot unofficial result could not be published for election {ElectionId} because the close eligibility snapshot is unavailable.",
                election.ElectionId);
            return null;
        }

        if (closeSnapshot.CountedParticipationCount != 0 || closeSnapshot.BlankCount != 0)
        {
            _logger.LogWarning(
                "[ElectionBallotPublicationService] Zero-ballot unofficial result could not be published for election {ElectionId} because the close eligibility snapshot reports counted participation.",
                election.ElectionId);
            return null;
        }

        var namedOptionResults = election.Options
            .Where(x => !x.IsBlankOption)
            .OrderBy(x => x.BallotOrder)
            .Select((option, index) => new ElectionResultOptionCount(
                option.OptionId,
                option.DisplayLabel,
                option.ShortDescription,
                option.BallotOrder,
                index + 1,
                VoteCount: 0))
            .ToArray();
        return await TryCreateParticipantEncryptedUnofficialResultAsync(
            repository,
            election,
            tallyReadyArtifact,
            namedOptionResults,
            blankCount: 0,
            totalVotedCount: 0,
            closeSnapshot,
            recordedAt,
            blockHeight,
            blockId);
    }

    private async Task<(bool IsSuccess, ElectionFinalizationShareRecord? ReleaseShare, string Error)> ResolveAdminOnlyProtectedReleaseShareAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        ElectionBoundaryArtifactRecord tallyReadyArtifact,
        ElectionCeremonyBindingSnapshot? ceremonySnapshot)
    {
        ceremonySnapshot ??= tallyReadyArtifact.CeremonySnapshot;
        if (ceremonySnapshot?.TallyPublicKey is not { Length: > 0 })
        {
            return (false, null, "Admin-only protected tally binding is missing the usable tally public key.");
        }

        BigInteger scalar;
        string error;
        var envelope = await repository.GetAdminOnlyProtectedTallyEnvelopeAsync(election.ElectionId);
        if (envelope is not null)
        {
            if (!ElectionProtectedTallyBinding.TryBuildAdminOnlyProtectedTallyBindingSnapshot(
                    election,
                    envelope,
                    _curve,
                    out var envelopeSnapshot,
                    out error))
            {
                return (false, null, error);
            }

            if (envelopeSnapshot?.TallyPublicKey is not { Length: > 0 } ||
                !CryptographicOperations.FixedTimeEquals(
                    envelopeSnapshot.TallyPublicKey,
                    ceremonySnapshot.TallyPublicKey))
            {
                return (false, null, "Admin-only protected tally envelope does not match the active ceremony binding.");
            }

            if (!ElectionProtectedTallyBinding.TryUnsealAdminOnlyProtectedTallyScalar(
                    election,
                    envelope,
                    _adminOnlyProtectedTallyEnvelopeCrypto,
                    _curve,
                    out scalar,
                    out error))
            {
                return (false, null, error);
            }
        }
        else
        {
            if (!ElectionProtectedTallyBinding.TryValidateAdminOnlyProtectedTallyPublicKey(
                    election,
                    _credentialsProvider,
                    _curve,
                    ceremonySnapshot.TallyPublicKey,
                    out error))
            {
                return (false, null, error);
            }

            if (!ElectionProtectedTallyBinding.TryDeriveAdminOnlyProtectedTallyScalar(
                    election,
                    _credentialsProvider,
                    _curve,
                    out scalar,
                    out error))
            {
                return (false, null, error);
            }
        }

        var shareMaterial = scalar.ToString(CultureInfo.InvariantCulture);
        var releaseShare = new ElectionFinalizationShareRecord(
            Guid.NewGuid(),
            Guid.Empty,
            election.ElectionId,
            election.OwnerPublicAddress,
            null,
            election.OwnerPublicAddress,
            ShareIndex: 1,
            ShareVersion: $"admin-only-protected-custody:{election.SelectedProfileId}",
            TargetType: ElectionFinalizationTargetType.AggregateTally,
            ClaimedCloseArtifactId: tallyReadyArtifact.Id,
            ClaimedAcceptedBallotSetHash: tallyReadyArtifact.AcceptedBallotSetHash ?? Array.Empty<byte>(),
            ClaimedFinalEncryptedTallyHash: tallyReadyArtifact.FinalEncryptedTallyHash ?? Array.Empty<byte>(),
            ClaimedTargetTallyId: $"admin-only-protected:{election.ElectionId}",
            ClaimedCeremonyVersionId: ceremonySnapshot.CeremonyVersionId,
            ClaimedTallyPublicKeyFingerprint: ceremonySnapshot.TallyPublicKeyFingerprint,
            CloseCountingJobId: null,
            ExecutorKeyAlgorithm: null,
            ShareMaterial: shareMaterial,
            ShareMaterialHash: Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(shareMaterial))),
            Status: ElectionFinalizationShareStatus.Accepted,
            FailureCode: null,
            FailureReason: null,
            SubmittedAt: DateTime.UtcNow,
            SourceTransactionId: null,
            SourceBlockHeight: null,
            SourceBlockId: null);
        return (true, releaseShare, string.Empty);
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

    private static ElectionBoundaryArtifactRecord? FindTallyReadyArtifact(
        ElectionRecord election,
        IReadOnlyList<ElectionBoundaryArtifactRecord> boundaryArtifacts) =>
        boundaryArtifacts.FirstOrDefault(x =>
            x.Id == election.TallyReadyArtifactId &&
            x.ArtifactType == ElectionBoundaryArtifactType.TallyReady) ??
        boundaryArtifacts
            .Where(x => x.ArtifactType == ElectionBoundaryArtifactType.TallyReady)
            .OrderByDescending(x => x.RecordedAt)
            .FirstOrDefault();

    private static bool TryBuildAdminOnlyDevModeResult(
        ElectionRecord election,
        IReadOnlyList<ElectionPublishedBallotRecord> publishedBallots,
        out AdminOnlyDevModeResult? result)
    {
        result = null;
        if (!ElectionDevModePublishedBallotSupport.TryBuildPublishedBallotTally(
                election,
                publishedBallots,
                out var tally) ||
            tally is null)
        {
            return false;
        }

        result = new AdminOnlyDevModeResult(
            tally.NamedOptionResults,
            tally.BlankCount,
            tally.TotalVotedCount,
            tally.FinalEncryptedTallyHash);
        return true;
    }

    // Close snapshots bind turnout denominator evidence, not per-voter ballot choice.
    // Blank-vote count must come from the anonymous tally/published ballot set instead.
    private static bool DoesCloseSnapshotReconcileWithAdminOnlyDevTurnout(
        ElectionEligibilitySnapshotRecord closeSnapshot,
        AdminOnlyDevModeResult devModeResult)
    {
        var expectedDidNotVoteCount = closeSnapshot.ActiveDenominatorCount - devModeResult.TotalVotedCount;
        return expectedDidNotVoteCount >= 0 &&
               closeSnapshot.CountedParticipationCount == devModeResult.TotalVotedCount &&
               closeSnapshot.DidNotVoteCount == expectedDidNotVoteCount;
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
        if (publishCount >= entries.Count)
        {
            return entries.ToArray();
        }

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

    private static IReadOnlyList<(ElectionBallotMemPoolRecord Entry, ElectionAcceptedBallotRecord AcceptedBallot)> ReorderSelectionsForPrivacy(
        IReadOnlyList<(ElectionBallotMemPoolRecord Entry, ElectionAcceptedBallotRecord AcceptedBallot)> selections)
    {
        if (selections.Count < 2)
        {
            return selections;
        }

        var acceptedOrderIds = selections
            .OrderBy(x => x.AcceptedBallot.AcceptedAt)
            .ThenBy(x => x.AcceptedBallot.Id)
            .Select(x => x.AcceptedBallot.Id)
            .ToArray();
        var currentOrderIds = selections
            .Select(x => x.AcceptedBallot.Id)
            .ToArray();

        if (!currentOrderIds.SequenceEqual(acceptedOrderIds))
        {
            return selections;
        }

        return selections
            .Skip(1)
            .Concat(selections.Take(1))
            .ToArray();
    }

    private static bool IsSupportedDevModeBallotPackageType(string? packageType) =>
        string.Equals(packageType, "dev-protected-ballot", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(packageType, "dev-published-ballot", StringComparison.OrdinalIgnoreCase);

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

    private static bool ByteArrayEquals(byte[]? left, byte[]? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    private static string ComputeHexSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));

    private static string SerializeResultArtifactPayload(
        string title,
        IReadOnlyList<ElectionResultOptionCount> namedOptionResults,
        int blankCount,
        int totalVotedCount,
        int eligibleToVoteCount,
        int didNotVoteCount,
        ElectionResultDenominatorEvidence denominatorEvidence) =>
        JsonSerializer.Serialize(
            new ResultArtifactPayload(
                title,
                namedOptionResults
                    .Select(x => new ResultOptionPayload(
                        x.OptionId,
                        x.DisplayLabel,
                        x.ShortDescription,
                        x.BallotOrder,
                        x.Rank,
                        x.VoteCount))
                    .ToArray(),
                blankCount,
                totalVotedCount,
                eligibleToVoteCount,
                didNotVoteCount,
                new ResultDenominatorEvidencePayload(
                    denominatorEvidence.SnapshotType.ToString(),
                    denominatorEvidence.EligibilitySnapshotId,
                    denominatorEvidence.BoundaryArtifactId,
                    Convert.ToHexString(denominatorEvidence.ActiveDenominatorSetHash))),
            ResultPayloadJsonOptions);

    private sealed record PublicationPayload(
        string EncryptedBallotPackage,
        string ProofBundle);

    private sealed record AdminOnlyDevModeBallotPackage(
        string? Mode,
        string? PackageType,
        string? ElectionId,
        string? ActorPublicAddress,
        string? OptionId,
        string? OptionLabel,
        string? OptionDescription,
        int BallotOrder,
        bool IsBlankOption,
        string? SelectionFingerprint,
        string? GeneratedAt,
        string? PublicationNonce);

    private sealed record PublishedDevModeProofBundle(
        string Mode,
        string ProofType,
        string PublicationVariant,
        string PublishedBallotHash);

    private sealed record AdminOnlyDevModeResult(
        IReadOnlyList<ElectionResultOptionCount> NamedOptionResults,
        int BlankCount,
        int TotalVotedCount,
        byte[] FinalEncryptedTallyHash);

    private sealed record ResultArtifactPayload(
        string Title,
        IReadOnlyList<ResultOptionPayload> NamedOptionResults,
        int BlankCount,
        int TotalVotedCount,
        int EligibleToVoteCount,
        int DidNotVoteCount,
        ResultDenominatorEvidencePayload DenominatorEvidence);

    private sealed record ResultOptionPayload(
        string OptionId,
        string DisplayLabel,
        string? ShortDescription,
        int BallotOrder,
        int Rank,
        int VoteCount);

    private sealed record ResultDenominatorEvidencePayload(
        string SnapshotType,
        Guid? EligibilitySnapshotId,
        Guid? BoundaryArtifactId,
        string ActiveDenominatorSetHashHex);
}
