using HushNode.Elections.Storage;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public interface IElectionDeploymentProofBindingService
{
    Task<ElectionDeploymentProofBindingResult> BindForOpenAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        ElectionBoundaryArtifactRecord openArtifact,
        CancellationToken cancellationToken = default);

    Task<ElectionDeploymentProofBindingResult> ReconcileCheckpointAsync(
        IElectionsRepository repository,
        ElectionDeploymentProofReconciliationRequest request,
        CancellationToken cancellationToken = default);

    Task<ElectionDeploymentProofPublicLedgerArtifactRecord?> BuildPublicLedgerArtifactAsync(
        IElectionsRepository repository,
        ElectionId electionId,
        CancellationToken cancellationToken = default);

    Task UpdatePublicLedgerArtifactReferenceAsync(
        IElectionsRepository repository,
        ElectionId electionId,
        string artifactRef,
        string artifactHash,
        DateTime recordedAtUtc,
        CancellationToken cancellationToken = default);
}

public sealed class ElectionDeploymentProofBindingService(
    IElectionDeploymentProofProfilePolicy profilePolicy,
    IActiveDeploymentProofProvider activeDeploymentProofProvider) : IElectionDeploymentProofBindingService
{
    private const string ProviderUnavailableCode = "deployment_proof_provider_unavailable";
    private const string ProviderEventLogUnavailableCode = "deployment_proof_event_log_unavailable";
    private const string ProviderProofFamilyUnavailableCode = "deployment_proof_family_unavailable";

    public Task<ElectionDeploymentProofBindingResult> BindForOpenAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        ElectionBoundaryArtifactRecord openArtifact,
        CancellationToken cancellationToken = default) =>
        CaptureCheckpointAsync(
            repository,
            new ElectionDeploymentProofReconciliationRequest(
                election,
                ElectionDeploymentProofCheckpointType.DraftToOpen,
                ElectionLifecycleState.Draft,
                ElectionLifecycleState.Open,
                openArtifact.RecordedAt,
                openArtifact.Id,
                ReportPackageId: null,
                openArtifact.SourceTransactionId,
                openArtifact.SourceBlockHeight,
                openArtifact.SourceBlockId),
            blockOnFailure: true,
            cancellationToken);

    public Task<ElectionDeploymentProofBindingResult> ReconcileCheckpointAsync(
        IElectionsRepository repository,
        ElectionDeploymentProofReconciliationRequest request,
        CancellationToken cancellationToken = default) =>
        CaptureCheckpointAsync(repository, request, blockOnFailure: false, cancellationToken);

    public async Task<ElectionDeploymentProofPublicLedgerArtifactRecord?> BuildPublicLedgerArtifactAsync(
        IElectionsRepository repository,
        ElectionId electionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        cancellationToken.ThrowIfCancellationRequested();

        var ledger = await repository.GetDeploymentProofLedgerAsync(electionId);
        if (ledger is null)
        {
            return null;
        }

        var checkpoints = await repository.GetDeploymentProofCheckpointsAsync(ledger.Id);
        var componentObservations = new List<ElectionDeploymentProofComponentObservationRecord>();
        var deploymentEvents = new List<ElectionDeploymentProofEventRecord>();
        var proofFamilies = new List<ElectionProofFamilyBindingStatusRecord>();
        foreach (var checkpoint in checkpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            componentObservations.AddRange(await repository.GetDeploymentProofComponentObservationsAsync(checkpoint.Id));
            deploymentEvents.AddRange(await repository.GetDeploymentProofEventsAsync(checkpoint.Id));
            proofFamilies.AddRange(await repository.GetProofFamilyBindingStatusesAsync(checkpoint.Id));
        }

        var latestCheckpoint = checkpoints
            .OrderByDescending(x => x.ObservedAtUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
        var finalStatus = ledger.FinalStatus ?? ledger.Status;
        var latestClaimEffect = latestCheckpoint?.ClaimEffect ??
            (finalStatus == ElectionDeploymentProofEvidenceStatus.NotRequired
                ? ElectionDeploymentProofClaimEffect.NotApplicable
                : ElectionDeploymentProofClaimEffect.NoClaim);

        return new ElectionDeploymentProofPublicLedgerArtifactRecord(
            ElectionDeploymentProofConstants.PublicLedgerArtifactSchemaId,
            electionId.ToString(),
            ledger.Id,
            ledger.LedgerPublicId,
            ledger.Status.ToString(),
            finalStatus.ToString(),
            latestClaimEffect.ToString(),
            ledger.BlocksDeploymentProofClaims || latestClaimEffect == ElectionDeploymentProofClaimEffect.Blocked,
            ResolvePublicLedgerClaimSummary(finalStatus, latestClaimEffect),
            ledger.DeploymentProfile,
            ledger.DeploymentProtocolVersion,
            ledger.PublicCatalogRepository,
            ledger.PublicCatalogRef,
            ledger.PublicCatalogCommit,
            ledger.PlatformCeremonyId,
            ledger.ActiveProofSetIdAtOpen,
            ledger.OpenedAtUtc,
            ledger.ClosedAtUtc,
            ledger.FinalizedAtUtc,
            ledger.VoidedAtUtc,
            ledger.LatestCheckpointId,
            ledger.CreatedAtUtc,
            ledger.LastReconciledAtUtc,
            BuildPublicClaimLimitations(componentObservations, proofFamilies),
            checkpoints
                .OrderBy(x => x.ObservedAtUtc)
                .ThenBy(x => x.CheckpointType)
                .ThenBy(x => x.Id)
                .Select(BuildPublicCheckpoint)
                .ToArray(),
            componentObservations
                .OrderBy(x => x.ObservedAtUtc)
                .ThenBy(x => x.ComponentId)
                .ThenBy(x => x.Id)
                .Select(BuildPublicComponentObservation)
                .ToArray(),
            deploymentEvents
                .OrderBy(x => x.OccurredAtUtc)
                .ThenBy(x => x.EventPublicId, StringComparer.Ordinal)
                .ThenBy(x => x.Id)
                .Select(BuildPublicDeploymentEvent)
                .ToArray(),
            proofFamilies
                .OrderBy(x => x.ObservedAtUtc)
                .ThenBy(x => x.ProofFamilyId, StringComparer.Ordinal)
                .ThenBy(x => x.Id)
                .Select(BuildPublicProofFamily)
                .ToArray(),
            PublicPrivacyBoundary:
            [
                "no_private_key",
                "no_raw_runtime_log",
                "no_voter_identity",
                "no_plaintext_vote",
                "no_trustee_share",
                "no_support_ticket_body",
                "restricted_evidence_refs_are_ids_or_hashes_only",
            ]);
    }

    public async Task UpdatePublicLedgerArtifactReferenceAsync(
        IElectionsRepository repository,
        ElectionId electionId,
        string artifactRef,
        string artifactHash,
        DateTime recordedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        cancellationToken.ThrowIfCancellationRequested();

        var ledger = await repository.GetDeploymentProofLedgerAsync(electionId);
        if (ledger is null)
        {
            return;
        }

        await repository.UpdateDeploymentProofLedgerAsync(ledger with
        {
            PublicLedgerArtifactRef = artifactRef,
            PublicLedgerArtifactHash = artifactHash,
            LastReconciledAtUtc = recordedAtUtc,
        });
    }

    private async Task<ElectionDeploymentProofBindingResult> CaptureCheckpointAsync(
        IElectionsRepository repository,
        ElectionDeploymentProofReconciliationRequest request,
        bool blockOnFailure,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(request);

        var profile = profilePolicy.ResolveProfile(request.Election);
        var latestCheckpoint = await repository.GetLatestDeploymentProofCheckpointAsync(request.Election.ElectionId);
        var capture = await CaptureActiveContextAsync(
            profile,
            latestCheckpoint?.ObservedAtUtc ?? DateTime.MinValue,
            request.ObservedAtUtc,
            cancellationToken);
        var policyResult = profilePolicy.EvaluateOpen(request.Election, capture.ActiveContext);
        var failureCodes = MergeFailureCodes(policyResult.FailureCodes, capture.ProviderErrorCodes);
        var effectiveEvidenceStatus = policyResult.EvidenceStatus;
        var effectiveClaimEffect = policyResult.ClaimEffect;

        (effectiveEvidenceStatus, effectiveClaimEffect) = ApplyEventImpact(
            effectiveEvidenceStatus,
            effectiveClaimEffect,
            capture.DeploymentEvents);

        if (capture.ProviderErrorCodes.Count > 0)
        {
            effectiveEvidenceStatus = ElectionDeploymentProofEvidenceStatus.Unknown;
            effectiveClaimEffect = ElectionDeploymentProofClaimEffect.Blocked;
        }

        var isAllowed = !blockOnFailure ||
            policyResult.IsOpenAllowed &&
            capture.ProviderErrorCodes.Count == 0 &&
            !effectiveEvidenceStatus.BlocksDeploymentProofClaims() &&
            effectiveClaimEffect != ElectionDeploymentProofClaimEffect.Blocked;

        if (blockOnFailure && !isAllowed)
        {
            return ElectionDeploymentProofBindingResult.Blocked(
                effectiveEvidenceStatus,
                effectiveClaimEffect,
                failureCodes.Count == 0 ? ["deployment_proof_blocked"] : failureCodes,
                ResolveOpenBlockedSummary(policyResult, capture.ProviderErrorCodes));
        }

        var existingCheckpoint = await FindExistingCheckpointAsync(repository, request);
        if (existingCheckpoint is not null)
        {
            return ElectionDeploymentProofBindingResult.Captured(
                existingCheckpoint.LedgerId,
                existingCheckpoint.Id,
                existingCheckpoint.EvidenceStatus,
                existingCheckpoint.ClaimEffect,
                existingCheckpoint.ProviderErrorCodes,
                existingCheckpoint.PublicSummary);
        }

        var ledger = await ResolveLedgerAsync(
            repository,
            request,
            profile,
            capture.ActiveContext,
            effectiveEvidenceStatus);
        var supersededCheckpoint = await repository.GetLatestDeploymentProofCheckpointAsync(
            request.Election.ElectionId,
            request.CheckpointType);
        var checkpoint = CreateCheckpoint(
            ledger,
            request,
            capture.ActiveContext,
            effectiveEvidenceStatus,
            effectiveClaimEffect,
            failureCodes,
            ResolveCheckpointSummary(request.CheckpointType, policyResult, capture.ProviderErrorCodes),
            supersededCheckpoint?.Id);

        await repository.SaveDeploymentProofCheckpointAsync(checkpoint);
        foreach (var observation in CreateComponentObservations(request, checkpoint.Id, capture.ActiveContext))
        {
            await repository.SaveDeploymentProofComponentObservationAsync(observation);
        }

        foreach (var deploymentEvent in CreateDeploymentEventRecords(request, checkpoint.Id, capture.DeploymentEvents))
        {
            await repository.SaveDeploymentProofEventAsync(deploymentEvent);
        }

        var proofFamily = await ResolveProofFamilyStatusAsync(
            capture.ActiveContext.ServerProof?.DeploymentProofId,
            request.ObservedAtUtc,
            cancellationToken);
        await repository.SaveProofFamilyBindingStatusAsync(CreateProofFamilyStatusRecord(
            request,
            checkpoint.Id,
            proofFamily));

        await repository.UpdateDeploymentProofLedgerAsync(UpdateLedgerFromCheckpoint(
            ledger,
            request,
            checkpoint,
            capture.ActiveContext,
            effectiveEvidenceStatus));

        return ElectionDeploymentProofBindingResult.Captured(
            ledger.Id,
            checkpoint.Id,
            effectiveEvidenceStatus,
            effectiveClaimEffect,
            failureCodes,
            checkpoint.PublicSummary);
    }

    private async Task<ActiveDeploymentProofCapture> CaptureActiveContextAsync(
        ElectionDeploymentProofProfile profile,
        DateTime sinceUtc,
        DateTime observedAtUtc,
        CancellationToken cancellationToken)
    {
        ActiveDeploymentProofContext activeContext;
        var providerErrorCodes = new List<string>();
        try
        {
            activeContext = await activeDeploymentProofProvider.GetActiveDeploymentProofContextAsync(
                profile,
                observedAtUtc,
                cancellationToken);
        }
        catch
        {
            providerErrorCodes.Add(ProviderUnavailableCode);
            activeContext = CreateUnknownContext(profile, observedAtUtc, ProviderUnavailableCode);
        }

        IReadOnlyList<ActiveDeploymentProofEvent> deploymentEvents;
        try
        {
            deploymentEvents = await activeDeploymentProofProvider.GetDeploymentEventsSinceAsync(
                profile,
                sinceUtc,
                observedAtUtc,
                cancellationToken);
        }
        catch
        {
            providerErrorCodes.Add(ProviderEventLogUnavailableCode);
            deploymentEvents = Array.Empty<ActiveDeploymentProofEvent>();
        }

        providerErrorCodes.AddRange(activeContext.ProviderErrors.Select(x => x.Code));
        return new ActiveDeploymentProofCapture(
            activeContext,
            deploymentEvents,
            providerErrorCodes
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }

    private async Task<ActiveProofFamilyStatus> ResolveProofFamilyStatusAsync(
        string? activeServerProofId,
        DateTime observedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            return await activeDeploymentProofProvider.ResolveProofFamilyStatusAsync(
                ElectionDeploymentProofConstants.RetentionLogPrivacyProofFamilyId,
                activeServerProofId,
                cancellationToken);
        }
        catch
        {
            return new ActiveProofFamilyStatus(
                ElectionDeploymentProofConstants.RetentionLogPrivacyProofFamilyId,
                ProofFamilyVersion: "v1",
                PackageId: null,
                PackageHash: null,
                PromotedRegisterRef: null,
                ElectionDeploymentProofConstants.Feat137SourceFeature,
                ElectionDeploymentProofEvidenceStatus.Unknown,
                ProviderProofFamilyUnavailableCode,
                "Deployment proof-family readiness could not be resolved.",
                observedAtUtc);
        }
    }

    private static ActiveDeploymentProofContext CreateUnknownContext(
        ElectionDeploymentProofProfile profile,
        DateTime observedAtUtc,
        string errorCode) =>
        new(
            ElectionDeploymentProofEvidenceStatus.Unknown,
            observedAtUtc,
            profile.ProfileId,
            ElectionDeploymentProofConstants.DeploymentProtocolVersion,
            PublicCatalogRef: null,
            PlatformCeremonyId: null,
            ServerProof: null,
            ExpectedWebClientProof: null,
            ProviderErrors:
            [
                new ActiveDeploymentProofProviderError(
                    errorCode,
                    "Active deployment proof provider could not be evaluated."),
            ]);

    private static async Task<ElectionDeploymentProofCheckpointRecord?> FindExistingCheckpointAsync(
        IElectionsRepository repository,
        ElectionDeploymentProofReconciliationRequest request)
    {
        var checkpoints = await repository.GetDeploymentProofCheckpointsAsync(request.Election.ElectionId);
        return checkpoints.FirstOrDefault(x =>
            x.CheckpointType == request.CheckpointType &&
            x.TransitionArtifactId == request.TransitionArtifactId &&
            x.ReportPackageId == request.ReportPackageId);
    }

    private static async Task<ElectionDeploymentProofLedgerRecord> ResolveLedgerAsync(
        IElectionsRepository repository,
        ElectionDeploymentProofReconciliationRequest request,
        ElectionDeploymentProofProfile profile,
        ActiveDeploymentProofContext activeContext,
        ElectionDeploymentProofEvidenceStatus status)
    {
        var existingLedger = await repository.GetDeploymentProofLedgerAsync(request.Election.ElectionId);
        if (existingLedger is not null)
        {
            return existingLedger;
        }

        var ledger = new ElectionDeploymentProofLedgerRecord(
            Guid.NewGuid(),
            request.Election.ElectionId,
            $"deployment-ledger-{request.Election.ElectionId}",
            ElectionDeploymentProofConstants.SchemaVersion,
            status,
            ElectionDeploymentProofLedgerVisibility.Public,
            profile.ProfileId,
            ResolveProtocolVersion(activeContext),
            PublicCatalogRepository: null,
            activeContext.PublicCatalogRef,
            PublicCatalogCommit: null,
            activeContext.PlatformCeremonyId,
            ActiveProofSetIdAtOpen: request.CheckpointType == ElectionDeploymentProofCheckpointType.DraftToOpen
                ? ResolveProofSetId(activeContext)
                : null,
            OpenedAtUtc: request.Election.OpenedAt,
            ClosedAtUtc: request.Election.ClosedAt,
            FinalizedAtUtc: request.Election.FinalizedAt,
            VoidedAtUtc: request.Election.LifecycleState == ElectionLifecycleState.Voided
                ? request.Election.LastUpdatedAt
                : null,
            LatestCheckpointId: null,
            FinalStatus: null,
            PublicLedgerArtifactRef: null,
            PublicLedgerArtifactHash: null,
            RestrictedEvidenceIndexRef: null,
            request.ObservedAtUtc,
            request.ObservedAtUtc);

        await repository.SaveDeploymentProofLedgerAsync(ledger);
        return ledger;
    }

    private static ElectionDeploymentProofCheckpointRecord CreateCheckpoint(
        ElectionDeploymentProofLedgerRecord ledger,
        ElectionDeploymentProofReconciliationRequest request,
        ActiveDeploymentProofContext activeContext,
        ElectionDeploymentProofEvidenceStatus evidenceStatus,
        ElectionDeploymentProofClaimEffect claimEffect,
        IReadOnlyList<string> providerErrorCodes,
        string publicSummary,
        Guid? supersededCheckpointId) =>
        new(
            Guid.NewGuid(),
            ledger.Id,
            request.Election.ElectionId,
            request.CheckpointType,
            request.SourceLifecycleState,
            request.TargetLifecycleState,
            request.TransitionArtifactId,
            request.ReportPackageId,
            ResolveProofSetId(activeContext),
            evidenceStatus,
            claimEffect,
            request.ObservedAtUtc,
            activeContext.ProviderStatus,
            providerErrorCodes,
            supersededCheckpointId,
            publicSummary,
            request.SourceTransactionId,
            request.SourceBlockHeight,
            request.SourceBlockId);

    private static IReadOnlyList<ElectionDeploymentProofComponentObservationRecord> CreateComponentObservations(
        ElectionDeploymentProofReconciliationRequest request,
        Guid checkpointId,
        ActiveDeploymentProofContext activeContext)
    {
        var observations = new List<ElectionDeploymentProofComponentObservationRecord>();
        if (activeContext.ServerProof is not null)
        {
            observations.Add(CreateComponentObservation(
                request,
                checkpointId,
                activeContext.ServerProof,
                expectedDeploymentProofId: activeContext.ServerProof.DeploymentProofId,
                expectedArtifactHash: activeContext.ServerProof.ArtifactHash,
                mismatchCode: ResolveMismatchCode(activeContext.ServerProof.EvidenceStatus)));
        }
        else
        {
            observations.Add(CreateMissingComponentObservation(
                request,
                checkpointId,
                ElectionDeploymentProofComponentId.HushServerNode,
                ElectionDeploymentProofEvidenceStatus.Missing,
                "server_deployment_proof_missing"));
        }

        observations.Add(CreateWebClientComponentObservation(request, checkpointId, activeContext));

        return observations;
    }

    private static ElectionDeploymentProofComponentObservationRecord CreateWebClientComponentObservation(
        ElectionDeploymentProofReconciliationRequest request,
        Guid checkpointId,
        ActiveDeploymentProofContext activeContext)
    {
        var expected = activeContext.ExpectedWebClientProof;
        var observed = activeContext.ObservedWebClientProof;
        var comparison = ResolveWebClientComparison(expected, observed);
        var sourceComponent = observed ?? expected;

        return new ElectionDeploymentProofComponentObservationRecord(
            Guid.NewGuid(),
            checkpointId,
            request.Election.ElectionId,
            ElectionDeploymentProofComponentId.HushWebClient,
            sourceComponent?.DeploymentProofId,
            expected?.DeploymentProofId,
            observed?.DeploymentProofId,
            expected?.ArtifactHash,
            observed?.ArtifactHash,
            comparison.EvidenceStatus,
            observed?.ObservationSource ?? ElectionDeploymentProofObservationSource.NotAvailable,
            sourceComponent?.SourceRef,
            sourceComponent?.ArtifactHash,
            sourceComponent?.PackageHash,
            sourceComponent?.PublicPackageRef,
            comparison.MismatchCode,
            sourceComponent?.SupersedesProofIds ?? Array.Empty<string>(),
            request.ObservedAtUtc);
    }

    private static ElectionDeploymentProofComponentObservationRecord CreateComponentObservation(
        ElectionDeploymentProofReconciliationRequest request,
        Guid checkpointId,
        ActiveDeploymentProofComponent component,
        string? expectedDeploymentProofId,
        string? expectedArtifactHash,
        string? mismatchCode) =>
        new(
            Guid.NewGuid(),
            checkpointId,
            request.Election.ElectionId,
            component.ComponentId,
            component.DeploymentProofId,
            expectedDeploymentProofId,
            component.DeploymentProofId,
            expectedArtifactHash,
            component.ArtifactHash,
            component.EvidenceStatus,
            component.ObservationSource,
            component.SourceRef,
            component.ArtifactHash,
            component.PackageHash,
            component.PublicPackageRef,
            mismatchCode,
            component.SupersedesProofIds,
            request.ObservedAtUtc);

    private static ElectionDeploymentProofComponentObservationRecord CreateMissingComponentObservation(
        ElectionDeploymentProofReconciliationRequest request,
        Guid checkpointId,
        ElectionDeploymentProofComponentId componentId,
        ElectionDeploymentProofEvidenceStatus evidenceStatus,
        string mismatchCode) =>
        new(
            Guid.NewGuid(),
            checkpointId,
            request.Election.ElectionId,
            componentId,
            DeploymentProofId: null,
            ExpectedDeploymentProofId: null,
            ObservedDeploymentProofId: null,
            ExpectedArtifactHash: null,
            ObservedArtifactHash: null,
            evidenceStatus,
            ElectionDeploymentProofObservationSource.NotAvailable,
            SourceRef: null,
            ArtifactHash: null,
            PackageHash: null,
            PublicPackageRef: null,
            mismatchCode,
            SupersedesProofIds: Array.Empty<string>(),
            request.ObservedAtUtc);

    private static IReadOnlyList<ElectionDeploymentProofEventRecord> CreateDeploymentEventRecords(
        ElectionDeploymentProofReconciliationRequest request,
        Guid checkpointId,
        IReadOnlyList<ActiveDeploymentProofEvent> deploymentEvents) =>
        deploymentEvents
            .Select(deploymentEvent => new ElectionDeploymentProofEventRecord(
                Guid.NewGuid(),
                checkpointId,
                request.Election.ElectionId,
                deploymentEvent.EventPublicId,
                deploymentEvent.EventType,
                deploymentEvent.DeploymentRunId,
                deploymentEvent.ComponentId,
                deploymentEvent.BeforeProofId,
                deploymentEvent.AfterProofId,
                deploymentEvent.Classification,
                deploymentEvent.Reason,
                deploymentEvent.ChecksRerun,
                deploymentEvent.CheckResult,
                deploymentEvent.AccountabilityMarker,
                deploymentEvent.OccurredAtUtc,
                deploymentEvent.EvidenceStatus))
            .ToArray();

    private static ElectionProofFamilyBindingStatusRecord CreateProofFamilyStatusRecord(
        ElectionDeploymentProofReconciliationRequest request,
        Guid checkpointId,
        ActiveProofFamilyStatus proofFamily) =>
        new(
            Guid.NewGuid(),
            checkpointId,
            request.Election.ElectionId,
            proofFamily.ProofFamilyId,
            proofFamily.ProofFamilyVersion,
            proofFamily.PackageId,
            proofFamily.PackageHash,
            proofFamily.PromotedRegisterRef,
            proofFamily.SourceFeature,
            proofFamily.EvidenceStatus,
            ResolveProofFamilyMismatchCode(proofFamily),
            proofFamily.PublicSummary,
            proofFamily.ObservedAtUtc);

    private static ElectionDeploymentProofLedgerRecord UpdateLedgerFromCheckpoint(
        ElectionDeploymentProofLedgerRecord ledger,
        ElectionDeploymentProofReconciliationRequest request,
        ElectionDeploymentProofCheckpointRecord checkpoint,
        ActiveDeploymentProofContext activeContext,
        ElectionDeploymentProofEvidenceStatus status) =>
        ledger with
        {
            Status = status,
            DeploymentProtocolVersion = ResolveProtocolVersion(activeContext),
            PublicCatalogRef = activeContext.PublicCatalogRef ?? ledger.PublicCatalogRef,
            PlatformCeremonyId = activeContext.PlatformCeremonyId ?? ledger.PlatformCeremonyId,
            ActiveProofSetIdAtOpen = request.CheckpointType == ElectionDeploymentProofCheckpointType.DraftToOpen
                ? checkpoint.ProofSetId
                : ledger.ActiveProofSetIdAtOpen,
            OpenedAtUtc = request.CheckpointType == ElectionDeploymentProofCheckpointType.DraftToOpen
                ? request.ObservedAtUtc
                : ledger.OpenedAtUtc ?? request.Election.OpenedAt,
            ClosedAtUtc = request.TargetLifecycleState == ElectionLifecycleState.Closed
                ? request.ObservedAtUtc
                : ledger.ClosedAtUtc ?? request.Election.ClosedAt,
            FinalizedAtUtc = request.TargetLifecycleState == ElectionLifecycleState.Finalized
                ? request.ObservedAtUtc
                : ledger.FinalizedAtUtc ?? request.Election.FinalizedAt,
            VoidedAtUtc = request.TargetLifecycleState == ElectionLifecycleState.Voided
                ? request.ObservedAtUtc
                : ledger.VoidedAtUtc,
            LatestCheckpointId = checkpoint.Id,
            FinalStatus = request.TargetLifecycleState is ElectionLifecycleState.Finalized or ElectionLifecycleState.Voided
                ? status
                : ledger.FinalStatus,
            LastReconciledAtUtc = request.ObservedAtUtc,
        };

    private static (ElectionDeploymentProofEvidenceStatus EvidenceStatus, ElectionDeploymentProofClaimEffect ClaimEffect)
        ApplyEventImpact(
            ElectionDeploymentProofEvidenceStatus evidenceStatus,
            ElectionDeploymentProofClaimEffect claimEffect,
            IReadOnlyList<ActiveDeploymentProofEvent> deploymentEvents)
    {
        if (deploymentEvents.Any(x =>
                x.Classification == ElectionDeploymentProofImpactClassification.UnknownPendingClassification ||
                x.EvidenceStatus == ElectionDeploymentProofEvidenceStatus.Unknown))
        {
            return (ElectionDeploymentProofEvidenceStatus.Unknown, ElectionDeploymentProofClaimEffect.Blocked);
        }

        if (deploymentEvents.Any(x =>
                x.Classification is
                    ElectionDeploymentProofImpactClassification.EmergencyChange or
                    ElectionDeploymentProofImpactClassification.VotingProtocolChange ||
                x.EvidenceStatus.BlocksDeploymentProofClaims()))
        {
            return (ElectionDeploymentProofEvidenceStatus.Blocked, ElectionDeploymentProofClaimEffect.Blocked);
        }

        if (deploymentEvents.Any(x => x.Classification == ElectionDeploymentProofImpactClassification.Rollback) &&
            claimEffect == ElectionDeploymentProofClaimEffect.Accepted)
        {
            return (ElectionDeploymentProofEvidenceStatus.Degraded, ElectionDeploymentProofClaimEffect.Downgraded);
        }

        return (evidenceStatus, claimEffect);
    }

    private static string ResolveProtocolVersion(ActiveDeploymentProofContext activeContext) =>
        string.IsNullOrWhiteSpace(activeContext.DeploymentProtocolVersion)
            ? ElectionDeploymentProofConstants.DeploymentProtocolVersion
            : activeContext.DeploymentProtocolVersion;

    private static string? ResolveProofSetId(ActiveDeploymentProofContext activeContext) =>
        activeContext.ServerProof?.DeploymentProofId ??
        activeContext.PublicCatalogRef ??
        activeContext.DeploymentTarget;

    private static string? ResolveMismatchCode(ElectionDeploymentProofEvidenceStatus status) =>
        status.BlocksDeploymentProofClaims()
            ? status.ToString().ToLowerInvariant()
            : null;

    private static WebClientComparison ResolveWebClientComparison(
        ActiveDeploymentProofComponent? expected,
        ActiveDeploymentProofComponent? observed)
    {
        if (observed is null)
        {
            return new WebClientComparison(
                ElectionDeploymentProofEvidenceStatus.NotYetSupported,
                ElectionDeploymentProofConstants.Feat144WebClientProofNotSupportedCode);
        }

        if (expected is null)
        {
            return new WebClientComparison(
                ElectionDeploymentProofEvidenceStatus.Mismatch,
                ElectionDeploymentProofConstants.Feat144WebClientExpectedProofMissingCode);
        }

        if (!string.Equals(expected.DeploymentProofId, observed.DeploymentProofId, StringComparison.Ordinal) ||
            !string.Equals(expected.ArtifactHash, observed.ArtifactHash, StringComparison.Ordinal))
        {
            return new WebClientComparison(
                ElectionDeploymentProofEvidenceStatus.Mismatch,
                ElectionDeploymentProofConstants.Feat144WebClientProofMismatchCode);
        }

        if (observed.EvidenceStatus.BlocksDeploymentProofClaims())
        {
            return new WebClientComparison(
                observed.EvidenceStatus,
                ResolveMismatchCode(observed.EvidenceStatus));
        }

        return new WebClientComparison(observed.EvidenceStatus, MismatchCode: null);
    }

    private static ElectionDeploymentProofClaimEffect ResolveProofFamilyClaimEffect(
        ElectionDeploymentProofEvidenceStatus status) =>
        status switch
        {
            ElectionDeploymentProofEvidenceStatus.Accepted => ElectionDeploymentProofClaimEffect.Accepted,
            ElectionDeploymentProofEvidenceStatus.AcceptedWithLimitations =>
                ElectionDeploymentProofClaimEffect.AcceptedWithLimitations,
            ElectionDeploymentProofEvidenceStatus.Degraded or
                ElectionDeploymentProofEvidenceStatus.Stale or
                ElectionDeploymentProofEvidenceStatus.Superseded =>
                ElectionDeploymentProofClaimEffect.Downgraded,
            ElectionDeploymentProofEvidenceStatus.NotRequired => ElectionDeploymentProofClaimEffect.NoClaim,
            _ => ElectionDeploymentProofClaimEffect.Blocked,
        };

    private static string? ResolveProofFamilyMismatchCode(ActiveProofFamilyStatus proofFamily)
    {
        if (!string.IsNullOrWhiteSpace(proofFamily.MismatchCode))
        {
            return proofFamily.MismatchCode.Trim();
        }

        return proofFamily.EvidenceStatus switch
        {
            ElectionDeploymentProofEvidenceStatus.Missing =>
                ElectionDeploymentProofConstants.PrivacyProofMissingCode,
            ElectionDeploymentProofEvidenceStatus.Stale =>
                ElectionDeploymentProofConstants.PrivacyProofStaleCode,
            ElectionDeploymentProofEvidenceStatus.Mismatch =>
                ElectionDeploymentProofConstants.PrivacyProofMismatchCode,
            ElectionDeploymentProofEvidenceStatus.Unknown =>
                ElectionDeploymentProofConstants.PrivacyProofUnknownCode,
            ElectionDeploymentProofEvidenceStatus.Blocked =>
                ElectionDeploymentProofConstants.PrivacyProofPrivateOnlyCode,
            _ => null,
        };
    }

    private static IReadOnlyList<string> BuildPublicClaimLimitations(
        IReadOnlyList<ElectionDeploymentProofComponentObservationRecord> componentObservations,
        IReadOnlyList<ElectionProofFamilyBindingStatusRecord> proofFamilies)
    {
        var limitations = new List<string>();
        var latestWebClientObservation = componentObservations
            .Where(x => x.ComponentId == ElectionDeploymentProofComponentId.HushWebClient)
            .OrderByDescending(x => x.ObservedAtUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();

        if (latestWebClientObservation?.EvidenceStatus ==
            ElectionDeploymentProofEvidenceStatus.NotYetSupported)
        {
            limitations.Add(
                "FEAT-144 WebClient proof handshake is not yet supported; complete WebClient proof binding remains downgraded for pilot handoff claims.");
        }
        else if (latestWebClientObservation?.EvidenceStatus == ElectionDeploymentProofEvidenceStatus.Mismatch)
        {
            limitations.Add(
                "FEAT-144 WebClient proof observation mismatches the expected catalog proof and blocks complete client proof binding claims.");
        }
        else if (latestWebClientObservation?.EvidenceStatus.BlocksDeploymentProofClaims() == true)
        {
            limitations.Add(
                "FEAT-144 WebClient proof observation blocks complete client proof binding claims.");
        }

        var latestPrivacyProof = proofFamilies
            .Where(x => string.Equals(
                x.ProofFamilyId,
                ElectionDeploymentProofConstants.RetentionLogPrivacyProofFamilyId,
                StringComparison.Ordinal))
            .OrderByDescending(x => x.ObservedAtUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();

        if (latestPrivacyProof is not null)
        {
            var claimEffect = ResolveProofFamilyClaimEffect(latestPrivacyProof.EvidenceStatus);
            if (claimEffect is ElectionDeploymentProofClaimEffect.Downgraded or
                ElectionDeploymentProofClaimEffect.Blocked or
                ElectionDeploymentProofClaimEffect.NoClaim)
            {
                limitations.Add(
                    $"FEAT-137 retention/log privacy proof-family status `{latestPrivacyProof.EvidenceStatus}` maps to `{claimEffect}` for privacy claims.");
            }
        }

        return limitations;
    }

    private static IReadOnlyList<string> MergeFailureCodes(
        IReadOnlyList<string> policyFailureCodes,
        IReadOnlyList<string> providerFailureCodes) =>
        policyFailureCodes
            .Concat(providerFailureCodes)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string ResolveOpenBlockedSummary(
        ElectionDeploymentProofOpenPolicyResult policyResult,
        IReadOnlyList<string> providerErrorCodes) =>
        providerErrorCodes.Count > 0
            ? "Active deployment proof could not be evaluated for election open."
            : policyResult.PublicSummary;

    private static string ResolveCheckpointSummary(
        ElectionDeploymentProofCheckpointType checkpointType,
        ElectionDeploymentProofOpenPolicyResult policyResult,
        IReadOnlyList<string> providerErrorCodes) =>
        providerErrorCodes.Count > 0
            ? "Deployment proof reconciliation completed with unavailable provider evidence."
            : checkpointType switch
            {
                ElectionDeploymentProofCheckpointType.DraftToOpen => policyResult.PublicSummary,
                ElectionDeploymentProofCheckpointType.OpenToClose => "Deployment proof reconciled for election close.",
                ElectionDeploymentProofCheckpointType.CloseToFinalize => "Deployment proof reconciled for clean finalization.",
                ElectionDeploymentProofCheckpointType.ClosedToFinalizedWithAnomaly =>
                    "Deployment proof reconciled for abnormal finalization.",
                ElectionDeploymentProofCheckpointType.OpenToVoid or ElectionDeploymentProofCheckpointType.CloseToVoid =>
                    "Deployment proof reconciled for owner void.",
                _ => "Deployment proof reconciled for lifecycle checkpoint.",
            };

    private static string ResolvePublicLedgerClaimSummary(
        ElectionDeploymentProofEvidenceStatus status,
        ElectionDeploymentProofClaimEffect claimEffect) =>
        status switch
        {
            ElectionDeploymentProofEvidenceStatus.Accepted =>
                "Deployment proof evidence is accepted for the lifecycle checkpoints represented in this ledger.",
            ElectionDeploymentProofEvidenceStatus.AcceptedWithLimitations =>
                "Deployment proof evidence is accepted with explicit limitations; the limitation is visible but does not by itself determine the election outcome.",
            ElectionDeploymentProofEvidenceStatus.Degraded =>
                "Deployment proof evidence is degraded; validation claims are downgraded while lifecycle and outcome evidence remain separate.",
            ElectionDeploymentProofEvidenceStatus.Blocked =>
                "Deployment proof evidence blocks deployment-proof validation claims; this does not by itself invalidate lifecycle or outcome evidence.",
            ElectionDeploymentProofEvidenceStatus.Missing =>
                "Required deployment proof evidence is missing, so deployment-proof validation claims are blocked.",
            ElectionDeploymentProofEvidenceStatus.Stale =>
                "Deployment proof evidence is stale and must be refreshed before making deployment-proof validation claims.",
            ElectionDeploymentProofEvidenceStatus.Superseded =>
                "Deployment proof evidence was superseded by a newer checkpoint or proof set.",
            ElectionDeploymentProofEvidenceStatus.Unknown =>
                "Deployment proof evidence is unknown pending provider or classification resolution.",
            ElectionDeploymentProofEvidenceStatus.NotRequired =>
                "Deployment proof evidence is not required for this election profile and no deployment-readiness claim is made.",
            ElectionDeploymentProofEvidenceStatus.Mismatch =>
                "Deployment proof evidence has a mismatch and blocks deployment-proof validation claims.",
            ElectionDeploymentProofEvidenceStatus.NotYetSupported =>
                "Deployment proof evidence is not yet supported for at least one required component.",
            _ when claimEffect == ElectionDeploymentProofClaimEffect.Blocked =>
                "Deployment proof claim effect is blocked.",
            _ => "Deployment proof evidence status is recorded for public verification.",
        };

    private static ElectionDeploymentProofPublicCheckpointArtifactRecord BuildPublicCheckpoint(
        ElectionDeploymentProofCheckpointRecord checkpoint) =>
        new(
            checkpoint.Id,
            checkpoint.CheckpointType.ToString(),
            checkpoint.SourceLifecycleState.ToString(),
            checkpoint.TargetLifecycleState.ToString(),
            checkpoint.TransitionArtifactId,
            checkpoint.ReportPackageId,
            checkpoint.ProofSetId,
            checkpoint.EvidenceStatus.ToString(),
            checkpoint.ProviderStatus.ToString(),
            checkpoint.ClaimEffect.ToString(),
            checkpoint.ProviderErrorCodes,
            checkpoint.SupersedesCheckpointId,
            checkpoint.PublicSummary,
            checkpoint.SourceTransactionId,
            checkpoint.SourceBlockHeight,
            checkpoint.SourceBlockId,
            checkpoint.ObservedAtUtc);

    private static ElectionDeploymentProofPublicComponentObservationArtifactRecord BuildPublicComponentObservation(
        ElectionDeploymentProofComponentObservationRecord observation) =>
        new(
            observation.Id,
            observation.CheckpointId,
            observation.ComponentId.ToString(),
            observation.DeploymentProofId,
            observation.ExpectedDeploymentProofId,
            observation.ObservedDeploymentProofId,
            observation.ExpectedArtifactHash,
            observation.ObservedArtifactHash,
            observation.EvidenceStatus.ToString(),
            observation.ObservationSource.ToString(),
            observation.SourceRef,
            observation.ArtifactHash,
            observation.PackageHash,
            observation.PublicPackageRef,
            observation.MismatchCode,
            observation.SupersedesProofIds,
            observation.ObservedAtUtc);

    private static ElectionDeploymentProofPublicEventArtifactRecord BuildPublicDeploymentEvent(
        ElectionDeploymentProofEventRecord deploymentEvent) =>
        new(
            deploymentEvent.Id,
            deploymentEvent.CheckpointId,
            deploymentEvent.EventPublicId,
            deploymentEvent.EventType,
            deploymentEvent.DeploymentRunId,
            deploymentEvent.ComponentId.ToString(),
            deploymentEvent.BeforeProofId,
            deploymentEvent.AfterProofId,
            deploymentEvent.Classification.ToString(),
            deploymentEvent.Reason,
            deploymentEvent.ChecksRerun,
            deploymentEvent.CheckResult,
            deploymentEvent.AccountabilityMarker,
            deploymentEvent.OccurredAtUtc,
            deploymentEvent.EvidenceStatus.ToString());

    private static ElectionDeploymentProofPublicProofFamilyArtifactRecord BuildPublicProofFamily(
        ElectionProofFamilyBindingStatusRecord proofFamily) =>
        new(
            proofFamily.Id,
            proofFamily.CheckpointId,
            proofFamily.ProofFamilyId,
            proofFamily.ProofFamilyVersion,
            proofFamily.PackageId,
            proofFamily.PackageHash,
            proofFamily.PromotedRegisterRef,
            proofFamily.SourceFeature,
            proofFamily.EvidenceStatus.ToString(),
            ResolveProofFamilyClaimEffect(proofFamily.EvidenceStatus).ToString(),
            proofFamily.MismatchCode,
            proofFamily.PublicSummary,
            proofFamily.ObservedAtUtc);

    private sealed record WebClientComparison(
        ElectionDeploymentProofEvidenceStatus EvidenceStatus,
        string? MismatchCode);

    private sealed record ActiveDeploymentProofCapture(
        ActiveDeploymentProofContext ActiveContext,
        IReadOnlyList<ActiveDeploymentProofEvent> DeploymentEvents,
        IReadOnlyList<string> ProviderErrorCodes);
}

public sealed class NoopElectionDeploymentProofBindingService : IElectionDeploymentProofBindingService
{
    public static NoopElectionDeploymentProofBindingService Instance { get; } = new();

    private NoopElectionDeploymentProofBindingService()
    {
    }

    public Task<ElectionDeploymentProofBindingResult> BindForOpenAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        ElectionBoundaryArtifactRecord openArtifact,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ElectionDeploymentProofBindingResult.NotCaptured());

    public Task<ElectionDeploymentProofBindingResult> ReconcileCheckpointAsync(
        IElectionsRepository repository,
        ElectionDeploymentProofReconciliationRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ElectionDeploymentProofBindingResult.NotCaptured());

    public Task<ElectionDeploymentProofPublicLedgerArtifactRecord?> BuildPublicLedgerArtifactAsync(
        IElectionsRepository repository,
        ElectionId electionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ElectionDeploymentProofPublicLedgerArtifactRecord?>(null);

    public Task UpdatePublicLedgerArtifactReferenceAsync(
        IElectionsRepository repository,
        ElectionId electionId,
        string artifactRef,
        string artifactHash,
        DateTime recordedAtUtc,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

public sealed record ElectionDeploymentProofReconciliationRequest(
    ElectionRecord Election,
    ElectionDeploymentProofCheckpointType CheckpointType,
    ElectionLifecycleState SourceLifecycleState,
    ElectionLifecycleState TargetLifecycleState,
    DateTime ObservedAtUtc,
    Guid? TransitionArtifactId,
    Guid? ReportPackageId,
    Guid? SourceTransactionId,
    long? SourceBlockHeight,
    Guid? SourceBlockId);

public sealed record ElectionDeploymentProofBindingResult(
    bool IsAllowed,
    bool WasCaptured,
    Guid? LedgerId,
    Guid? CheckpointId,
    ElectionDeploymentProofEvidenceStatus EvidenceStatus,
    ElectionDeploymentProofClaimEffect ClaimEffect,
    IReadOnlyList<string> FailureCodes,
    string PublicSummary)
{
    public static ElectionDeploymentProofBindingResult Captured(
        Guid ledgerId,
        Guid checkpointId,
        ElectionDeploymentProofEvidenceStatus evidenceStatus,
        ElectionDeploymentProofClaimEffect claimEffect,
        IReadOnlyList<string> failureCodes,
        string publicSummary) =>
        new(
            IsAllowed: true,
            WasCaptured: true,
            ledgerId,
            checkpointId,
            evidenceStatus,
            claimEffect,
            NormalizeCodes(failureCodes),
            publicSummary);

    public static ElectionDeploymentProofBindingResult Blocked(
        ElectionDeploymentProofEvidenceStatus evidenceStatus,
        ElectionDeploymentProofClaimEffect claimEffect,
        IReadOnlyList<string> failureCodes,
        string publicSummary) =>
        new(
            IsAllowed: false,
            WasCaptured: false,
            LedgerId: null,
            CheckpointId: null,
            evidenceStatus,
            claimEffect,
            NormalizeCodes(failureCodes),
            publicSummary);

    public static ElectionDeploymentProofBindingResult NotCaptured() =>
        new(
            IsAllowed: true,
            WasCaptured: false,
            LedgerId: null,
            CheckpointId: null,
            ElectionDeploymentProofEvidenceStatus.NotRequired,
            ElectionDeploymentProofClaimEffect.NotApplicable,
            Array.Empty<string>(),
            "Deployment proof binding was not captured for this execution context.");

    private static IReadOnlyList<string> NormalizeCodes(IReadOnlyList<string>? failureCodes) =>
        failureCodes is null
            ? Array.Empty<string>()
            : failureCodes
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
}
