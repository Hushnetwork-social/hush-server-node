using System.Text.Json;
using HushNetwork.proto;
using HushNode.Elections;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using HushShared.Elections.PublicationProof;
using HushShared.Elections.Verification.Model;

namespace HushNode.Elections.gRPC;

public partial class ElectionQueryApplicationService
{
    private const string VerifierResultNotAvailableCode = "verifier_result_not_available";
    private static readonly string[] Sp08EvidenceFileNames =
    [
        VerificationPackageFileNames.Sp08ReleaseManifest,
        VerificationPackageFileNames.Sp08ReleaseIntegrity,
        VerificationPackageFileNames.Sp08ReleaseIntegrityVerifierOutput,
    ];
    private static readonly string[] Sp09PublicEvidenceFileNames =
    [
        VerificationPackageFileNames.Sp09ExternalReviewStatus,
        VerificationPackageFileNames.Sp09ExternalReviewClaimTable,
        VerificationPackageFileNames.Sp09ExternalReviewVerifierOutput,
    ];
    private static readonly string[] Sp09RestrictedEvidenceFileNames =
    [
        VerificationPackageFileNames.RestrictedSp09FindingTracker,
        VerificationPackageFileNames.RestrictedSp09RetestEvidence,
        VerificationPackageFileNames.RestrictedSp09ReportReference,
    ];

    public async Task<GetElectionVerificationPackageStatusResponse> GetElectionVerificationPackageStatusAsync(
        ElectionId electionId,
        string actorPublicAddress)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var normalizedActorPublicAddress = actorPublicAddress?.Trim() ?? string.Empty;

        var election = await repository.GetElectionAsync(electionId);
        if (election is null)
        {
            return new GetElectionVerificationPackageStatusResponse
            {
                Success = false,
                ErrorMessage = $"Election {electionId} was not found.",
                ElectionId = electionId.ToString(),
                ActorPublicAddress = normalizedActorPublicAddress,
            };
        }

        var context = await LoadVerificationPackageContextAsync(repository, election, normalizedActorPublicAddress);
        return new GetElectionVerificationPackageStatusResponse
        {
            Success = true,
            ErrorMessage = string.Empty,
            ElectionId = election.ElectionId.ToString(),
            ActorPublicAddress = normalizedActorPublicAddress,
            Status = BuildVerificationPackageStatusView(context, includePackageHashes: true),
        };
    }

    public async Task<ExportElectionVerificationPackageResponse> ExportElectionVerificationPackageAsync(
        ElectionId electionId,
        string actorPublicAddress,
        ElectionVerificationPackageViewProto packageView)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var normalizedActorPublicAddress = actorPublicAddress?.Trim() ?? string.Empty;
        var domainPackageView = packageView.ToDomain();

        var election = await repository.GetElectionAsync(electionId);
        if (election is null)
        {
            return new ExportElectionVerificationPackageResponse
            {
                Success = false,
                ErrorMessage = $"Election {electionId} was not found.",
                ElectionId = electionId.ToString(),
                ActorPublicAddress = normalizedActorPublicAddress,
                PackageView = packageView,
                Blocker = ElectionVerificationPackageBlockerProto.VerificationPackageBlockerMissingPackage,
                ResultCode = VerificationResultCodes.PackageManifestMissingArtifact,
            };
        }

        var context = await LoadVerificationPackageContextAsync(repository, election, normalizedActorPublicAddress);
        var availability = BuildPackageAvailability(context, domainPackageView, includePackageHash: false);
        if (!availability.IsAvailable)
        {
            return new ExportElectionVerificationPackageResponse
            {
                Success = false,
                ErrorMessage = availability.Message,
                ElectionId = election.ElectionId.ToString(),
                ActorPublicAddress = normalizedActorPublicAddress,
                PackageView = packageView,
                Blocker = availability.Blocker,
                ResultCode = availability.BlockerCode,
            };
        }

        var result = _verificationPackageExportService.Export(BuildExportRequest(context, domainPackageView));
        var response = new ExportElectionVerificationPackageResponse
        {
            Success = result.Success,
            ErrorMessage = result.Success ? string.Empty : result.Message,
            ElectionId = election.ElectionId.ToString(),
            ActorPublicAddress = normalizedActorPublicAddress,
            PackageView = packageView,
            Blocker = result.Success
                ? ElectionVerificationPackageBlockerProto.VerificationPackageBlockerNone
                : MapExportBlocker(result.Code),
            ResultCode = result.Code,
            PackageId = result.PackageId ?? string.Empty,
            PackageHash = result.PackageHash ?? string.Empty,
        };
        response.Files.AddRange(result.Files.Select(x => x.ToProto()));
        return response;
    }

    private ElectionVerificationPackageStatusView? BuildVerificationPackageStatusFromLoadedResultView(
        ElectionRecord election,
        string actorPublicAddress,
        bool isOwner,
        bool acceptedTrustee,
        bool isDesignatedAuditor,
        ElectionReportPackageRecord? latestReportPackage,
        ProtocolPackageBindingRecord? protocolPackageBinding)
    {
        var context = new VerificationPackageContext(
            election,
            actorPublicAddress,
            isOwner,
            acceptedTrustee,
            isDesignatedAuditor,
            latestReportPackage,
            protocolPackageBinding,
            TrusteeInvitations: [],
            ReportArtifacts: [],
            BoundaryArtifacts: [],
            AcceptedBallots: [],
            PublishedBallots: [],
            FinalizationSessions: [],
            FinalizationShares: [],
            ReleaseEvidenceRecords: [],
            CeremonyVersions: [],
            CeremonyTrusteeStates: [],
            CeremonyShareCustodyRecords: [],
            RosterEntries: [],
            ParticipationRecords: [],
            VoterCeremonyRecords: [],
            PreparedBallotCommitments: [],
            SpoiledPreparedBallots: [],
            RosterImportEvidences: [],
            EligibilityPolicyEvidences: [],
            CommitmentSchemeEvidences: [],
            CommitmentRegistrations: [],
            CheckoffConsumptions: [],
            EligibilityActivationEvents: [],
            PublicationWitnesses: [],
            PublicationProofSessions: [],
            PublicationProofTranscripts: [],
            PublicationWitnessDeletionReceipts: []);

        return BuildVerificationPackageStatusView(context, includePackageHashes: false);
    }

    private async Task<VerificationPackageContext> LoadVerificationPackageContextAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        string actorPublicAddress)
    {
        var trusteeInvitations = await repository.GetTrusteeInvitationsAsync(election.ElectionId);
        var reportAccessGrant = string.IsNullOrWhiteSpace(actorPublicAddress)
            ? null
            : await repository.GetReportAccessGrantAsync(election.ElectionId, actorPublicAddress);
        var isOwner = !string.IsNullOrWhiteSpace(actorPublicAddress) &&
            string.Equals(election.OwnerPublicAddress, actorPublicAddress, StringComparison.OrdinalIgnoreCase);
        var acceptedTrustee = trusteeInvitations.Any(x =>
            x.Status == ElectionTrusteeInvitationStatus.Accepted &&
            string.Equals(x.TrusteeUserAddress, actorPublicAddress, StringComparison.OrdinalIgnoreCase));
        var isDesignatedAuditor = reportAccessGrant?.GrantRole == ElectionReportAccessGrantRole.DesignatedAuditor;
        var latestReportPackage = await repository.GetLatestReportPackageAsync(election.ElectionId);
        var protocolPackageBinding = IsVerificationPackageVisible(isOwner, acceptedTrustee, isDesignatedAuditor)
            ? await repository.GetSealedProtocolPackageBindingAsync(election.ElectionId) ??
              await repository.GetLatestProtocolPackageBindingAsync(election.ElectionId)
            : null;

        var reportArtifacts = latestReportPackage?.Status == ElectionReportPackageStatus.Sealed
            ? await repository.GetReportArtifactsAsync(latestReportPackage.Id)
            : Array.Empty<ElectionReportArtifactRecord>();
        var boundaryArtifacts = await repository.GetBoundaryArtifactsAsync(election.ElectionId);
        var acceptedBallots = await repository.GetAcceptedBallotsAsync(election.ElectionId);
        var publishedBallots = await repository.GetPublishedBallotsAsync(election.ElectionId);
        var finalizationSessions = await repository.GetFinalizationSessionsAsync(election.ElectionId);
        var finalizationShares = new List<ElectionFinalizationShareRecord>();
        foreach (var session in finalizationSessions)
        {
            finalizationShares.AddRange(await repository.GetFinalizationSharesAsync(session.Id));
        }

        var releaseEvidence = await repository.GetFinalizationReleaseEvidenceRecordsAsync(election.ElectionId);
        var ceremonyVersions = await repository.GetCeremonyVersionsAsync(election.ElectionId);
        var ceremonyTrusteeStates = new List<ElectionCeremonyTrusteeStateRecord>();
        var ceremonyShareCustodyRecords = new List<ElectionCeremonyShareCustodyRecord>();
        foreach (var ceremonyVersion in ceremonyVersions)
        {
            ceremonyTrusteeStates.AddRange(await repository.GetCeremonyTrusteeStatesAsync(ceremonyVersion.Id));
            ceremonyShareCustodyRecords.AddRange(await repository.GetCeremonyShareCustodyRecordsAsync(ceremonyVersion.Id));
        }

        var rosterEntries = await repository.GetRosterEntriesAsync(election.ElectionId);
        var participationRecords = await repository.GetParticipationRecordsAsync(election.ElectionId);
        var voterCeremonyRecords = await repository.GetVoterCeremonyRecordsAsync(election.ElectionId);
        var preparedBallotCommitments = await repository.GetPreparedBallotCommitmentsAsync(election.ElectionId);
        var spoiledPreparedBallots = await repository.GetSpoiledPreparedBallotsAsync(election.ElectionId);
        var rosterImportEvidences = await repository.GetRosterImportEvidencesAsync(election.ElectionId);
        var eligibilityPolicyEvidences = await repository.GetEligibilityPolicyEvidencesAsync(election.ElectionId);
        var commitmentSchemeEvidences = await repository.GetCommitmentSchemeEvidencesAsync(election.ElectionId);
        var commitmentRegistrations = await repository.GetCommitmentRegistrationsAsync(election.ElectionId);
        var checkoffConsumptions = await repository.GetCheckoffConsumptionsAsync(election.ElectionId);
        var eligibilityActivationEvents = await repository.GetEligibilityActivationEventsAsync(election.ElectionId);
        var publicationWitnesses = await repository.GetPublicationWitnessesAsync(election.ElectionId);
        var publicationProofSessions = await repository.GetPublicationProofSessionsAsync(election.ElectionId);
        var publicationProofTranscripts = await repository.GetPublicationProofTranscriptsAsync(election.ElectionId);
        var publicationWitnessDeletionReceipts =
            await repository.GetPublicationWitnessDeletionReceiptsAsync(election.ElectionId);

        return new VerificationPackageContext(
            election,
            actorPublicAddress,
            isOwner,
            acceptedTrustee,
            isDesignatedAuditor,
            latestReportPackage,
            protocolPackageBinding,
            trusteeInvitations,
            reportArtifacts,
            boundaryArtifacts,
            acceptedBallots,
            publishedBallots,
            finalizationSessions,
            finalizationShares,
            releaseEvidence,
            ceremonyVersions,
            ceremonyTrusteeStates,
            ceremonyShareCustodyRecords,
            rosterEntries,
            participationRecords,
            voterCeremonyRecords,
            preparedBallotCommitments,
            spoiledPreparedBallots,
            rosterImportEvidences,
            eligibilityPolicyEvidences,
            commitmentSchemeEvidences,
            commitmentRegistrations,
            checkoffConsumptions,
            eligibilityActivationEvents,
            publicationWitnesses,
            publicationProofSessions,
            publicationProofTranscripts,
            publicationWitnessDeletionReceipts);
    }

    private ElectionVerificationPackageStatusView BuildVerificationPackageStatusView(
        VerificationPackageContext context,
        bool includePackageHashes)
    {
        var publicPackage = BuildPackageAvailability(
            context,
            VerificationPackageView.PublicAnonymous,
            includePackageHashes);
        var restrictedPackage = BuildPackageAvailability(
            context,
            VerificationPackageView.RestrictedOwnerAuditor,
            includePackageHashes);
        var status = ResolvePackageStatus(context);
        var view = new ElectionVerificationPackageStatusView
        {
            ElectionId = context.Election.ElectionId.ToString(),
            ActorPublicAddress = context.ActorPublicAddress,
            IsVisible = context.CanViewPackageStatus,
            Status = status,
            StatusMessage = ResolvePackageStatusMessage(status, context),
            PublicPackage = publicPackage,
            RestrictedPackage = restrictedPackage,
            LastVerifierResult = BuildVerifierResultNotAvailable(),
            Sp04Evidence = BuildSp04EvidenceStatus(context),
            Sp05Evidence = BuildSp05EvidenceStatus(context),
        };
        view.Sp06Evidence = BuildSp06EvidenceStatus(context, publicPackage.IsAvailable, restrictedPackage.IsAvailable);
        view.Sp07Evidence = BuildSp07EvidenceStatus(context, publicPackage.IsAvailable, restrictedPackage.IsAvailable);
        view.Sp08ReleaseIntegrity = BuildSp08ReleaseIntegrityStatus(
            context,
            publicPackage.IsAvailable,
            restrictedPackage.IsAvailable,
            includePackageHashes);
        view.Sp09ExternalReview = BuildSp09ExternalReviewStatus(
            context,
            publicPackage.IsAvailable,
            restrictedPackage.IsAvailable,
            includePackageHashes);

        if (context.CanViewPackageStatus && context.ProtocolPackageBinding is not null)
        {
            view.ProtocolPackageBinding = context.ProtocolPackageBinding.ToProto();
        }

        return view;
    }

    private static ElectionSp06EvidenceStatusView BuildSp06EvidenceStatus(
        VerificationPackageContext context,
        bool publicPackageAvailable,
        bool restrictedPackageAvailable)
    {
        var expected = IsSp06EvidenceExpected(context.Election);
        var controlDomains = BuildSp06ControlDomainRecords(context);
        var ceremonyVersion = ResolveSp06CeremonyVersion(context);
        var thresholdProfile = BuildSp06ThresholdProfile(context.Election, ceremonyVersion);
        var requiredTrustees = ceremonyVersion?.BoundTrustees ??
            Array.Empty<HushShared.Elections.Model.ElectionTrusteeReference>();
        var controlSummary = expected
            ? ElectionSp06ControlDomainPolicy.EvaluateHighAssuranceV1(
                context.Election,
                thresholdProfile,
                requiredTrustees,
                controlDomains)
            : null;
        var latestSession = context.FinalizationSessions
            .OrderByDescending(x => x.CompletedAt ?? x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
        var sessionShares = latestSession is null
            ? Array.Empty<ElectionFinalizationShareRecord>()
            : context.FinalizationShares
                .Where(x => x.FinalizationSessionId == latestSession.Id)
                .ToArray();
        var acceptedReleaseCount = sessionShares.Count(x => x.Status == ElectionFinalizationShareStatus.Accepted);
        var rejectedReleaseCount = sessionShares.Count(x => x.Status == ElectionFinalizationShareStatus.Rejected);
        var trusteeCount = expected
            ? ElectionSp06ControlDomainPolicy.HighAssuranceV1TrusteeCount
            : latestSession?.EligibleTrustees.Count ?? 0;
        var threshold = expected
            ? ElectionSp06ControlDomainPolicy.HighAssuranceV1Threshold
            : latestSession?.RequiredShareCount ?? context.Election.RequiredApprovalCount ?? 0;
        var missingReleaseCount = latestSession is null
            ? 0
            : Math.Max(
                0,
                latestSession.EligibleTrustees.Count - sessionShares
                    .Select(x => x.TrusteeUserAddress)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count());
        var missingEvidenceCount = expected
            ? Math.Max(
                controlSummary?.MissingEvidenceCount ?? 0,
                ElectionSp06ControlDomainPolicy.HighAssuranceV1TrusteeCount - controlDomains.Count)
            : 0;
        var staleEvidenceCount = controlSummary?.StaleEvidenceCount ?? 0;
        var incompatibleEvidenceCount = controlSummary?.IncompatibleEvidenceCount ?? 0;
        var incompleteControlEvidence =
            missingEvidenceCount > 0 ||
            staleEvidenceCount > 0 ||
            incompatibleEvidenceCount > 0 ||
            controlDomains.Count(x => !x.AcceptedBeforeOpen) > 0;
        var latestCtrlCode = !expected
            ? VerificationResultCodes.PackageStructureValid
            : incompleteControlEvidence
                ? VerificationResultCodes.TrusteeAcceptanceIncomplete
                : acceptedReleaseCount < ElectionSp06ControlDomainPolicy.HighAssuranceV1Threshold
                    ? VerificationResultCodes.TrusteeReleaseThresholdNotMet
                    : VerificationResultCodes.TrusteeControlDomainEvidenceValid;
        var message = !context.CanViewPackageStatus
            ? "SP-06 trustee evidence status is not visible to this actor."
            : !expected
                ? "SP-06 high-assurance trustee control profile is not claimed by this election."
                : publicPackageAvailable
                    ? "SP-06 trustee evidence is available for verification package export."
                    : "SP-06 trustee evidence is being collected and becomes exportable after finalization.";

        var view = new ElectionSp06EvidenceStatusView
        {
            EvidenceExpected = expected,
            PublicEvidenceAvailable = expected && publicPackageAvailable,
            RestrictedEvidenceAvailable = expected && restrictedPackageAvailable && context.CanExportRestrictedPackage,
            ControlDomainProfileId = context.Election.ControlDomainProfileId ??
                (expected ? ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1 : string.Empty),
            ControlDomainProfileVersion = context.Election.ControlDomainProfileVersion ??
                (expected ? ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1Version : string.Empty),
            ThresholdProfileId = context.Election.ThresholdProfileId ?? context.Election.SelectedProfileId,
            TrusteeCount = trusteeCount,
            TrusteeThreshold = threshold,
            AcceptedBeforeOpenCount = controlSummary?.AcceptedBeforeOpenCount ?? 0,
            CompleteEvidenceCount = controlSummary?.CompleteEvidenceCount ?? 0,
            MissingEvidenceCount = missingEvidenceCount,
            StaleEvidenceCount = staleEvidenceCount,
            IncompatibleEvidenceCount = incompatibleEvidenceCount,
            AcceptedReleaseArtifactCount = acceptedReleaseCount,
            MissingReleaseArtifactCount = missingReleaseCount,
            RejectedReleaseArtifactCount = rejectedReleaseCount,
            LatestCtrlResultCode = latestCtrlCode,
            Message = message,
        };

        if (controlSummary is not null)
        {
            view.Blockers.AddRange(controlSummary.ReadinessBlockers.Select(x => new ElectionSp06ReadinessBlockerView
            {
                Code = x.Code,
                Message = x.Message,
                TrusteeRef = x.TrusteeId ?? string.Empty,
                BlocksOpen = x.BlocksOpen,
                BlocksFinalization = x.BlocksFinalization,
            }));
        }

        if (expected &&
            missingEvidenceCount > 0 &&
            view.Blockers.All(x => x.Code != "control_domain_evidence_missing"))
        {
            view.Blockers.Add(new ElectionSp06ReadinessBlockerView
            {
                Code = "control_domain_evidence_missing",
                Message = "SP-06 high-assurance trustee control-domain evidence is not yet persisted for every required trustee.",
                TrusteeRef = string.Empty,
                BlocksOpen = true,
                BlocksFinalization = false,
            });
        }

        if (expected &&
            context.Election.LifecycleState == ElectionLifecycleState.Finalized &&
            acceptedReleaseCount < ElectionSp06ControlDomainPolicy.HighAssuranceV1Threshold)
        {
            view.Blockers.Add(new ElectionSp06ReadinessBlockerView
            {
                Code = "trustee_release_threshold_not_met",
                Message = "SP-06 high-assurance finalization has fewer than three accepted trustee release artifacts.",
                TrusteeRef = string.Empty,
                BlocksOpen = false,
                BlocksFinalization = true,
            });
        }

        return view;
    }

    private static ElectionSp07EvidenceStatusView BuildSp07EvidenceStatus(
        VerificationPackageContext context,
        bool publicPackageAvailable,
        bool restrictedPackageAvailable)
    {
        var expected = IsSp07EvidenceExpected(context.Election);
        var latestSession = context.PublicationProofSessions
            .OrderByDescending(x => x.CompletedAt ?? x.StartedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
        var latestTranscript = context.PublicationProofTranscripts
            .OrderByDescending(x => x.GeneratedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
        var latestDeletionReceipt = context.PublicationWitnessDeletionReceipts
            .OrderByDescending(x => x.DeletedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
        var acceptedBallotCount = latestTranscript?.AcceptedBallotCount ??
            latestSession?.AcceptedBallotCount ??
            context.AcceptedBallots.Count;
        var publishedBallotCount = latestTranscript?.PublishedBallotCount ??
            latestSession?.PublishedBallotCount ??
            context.PublishedBallots.Count;
        var ciphertextSlotCount = latestTranscript?.CiphertextSlotCount ??
            context.Election.Options.Count;
        var plannedChunkCount = TryCreateSp07ChunkPlan(
            acceptedBallotCount,
            ciphertextSlotCount,
            out var chunkPlanningFailureMessage)?.Chunks.Count ?? 0;
        var chunkCount = latestSession?.ChunkCount ??
            (plannedChunkCount > 0 ? plannedChunkCount : publishedBallotCount > 0 ? 1 : 0);
        var latestManifest = TryReadSp07Manifest(latestTranscript);
        var completedChunkCount = latestManifest?.CompletedChunkCount ??
            ResolveCompletedSp07ChunkCount(latestSession, chunkCount);
        var failedChunkCount = latestManifest?.FailedChunkCount ??
            ResolveFailedSp07ChunkCount(latestSession, chunkCount);
        var slowestChunkMilliseconds = latestManifest?.SlowestChunkMilliseconds ?? 0;
        var latestPubCode = ResolveSp07ResultCode(expected, latestSession, latestTranscript, latestDeletionReceipt);
        var verified = string.Equals(
            latestPubCode,
            VerificationResultCodes.PublicationProofEvidenceValid,
            StringComparison.Ordinal);

        var view = new ElectionSp07EvidenceStatusView
        {
            EvidenceExpected = expected,
            PublicEvidenceAvailable = expected && publicPackageAvailable && verified,
            RestrictedEvidenceAvailable = expected && restrictedPackageAvailable && context.CanExportRestrictedPackage,
            PublicationProofMode = latestTranscript?.ProofMode ??
                latestSession?.ProofMode ??
                ElectionSp07ProfileIds.PublicationProofMode,
            ProofConstruction = latestTranscript?.ProofConstruction ??
                latestSession?.ProofConstruction ??
                ElectionSp07ProfileIds.ProofConstruction,
            StatementId = latestTranscript?.StatementId ??
                latestSession?.StatementId ??
                ElectionSp07ProfileIds.StatementId,
            ExternalReviewStatus = latestTranscript?.ExternalReviewStatus ??
                ElectionSp07ProfileIds.ExternalReviewStatus,
            AcceptedBallotCount = acceptedBallotCount,
            PublishedBallotCount = publishedBallotCount,
            CiphertextSlotCount = ciphertextSlotCount,
            ChunkCount = chunkCount,
            AcceptedBallotSetHash = latestTranscript?.AcceptedBallotSetHash ??
                latestSession?.AcceptedBallotSetHash ??
                ComputeAcceptedBallotSetHash(context.AcceptedBallots),
            PublishedBallotStreamHash = latestTranscript?.PublishedBallotStreamHash ??
                latestSession?.PublishedBallotStreamHash ??
                ComputePublishedBallotStreamHash(context.PublishedBallots),
            TranscriptHash = latestTranscript?.TranscriptHash ??
                latestSession?.TranscriptHash ??
                string.Empty,
            ProofHash = latestTranscript?.ProofHash ??
                latestSession?.ProofHash ??
                string.Empty,
            WitnessDeletionReceiptHash = latestDeletionReceipt is null
                ? string.Empty
                : ComputeWitnessDeletionReceiptHash(latestDeletionReceipt),
            LatestPubResultCode = latestPubCode,
            ProgressStatus = ResolveSp07ProgressStatus(context.Election, latestSession),
            CanRetry = context.IsOwner &&
                latestSession?.Status == ElectionPublicationProofSessionStatus.Failed &&
                context.PublicationWitnesses.Any(x =>
                    x.WitnessSetId == latestSession.WitnessSetId &&
                    x.CustodyStatus == ElectionPublicationWitnessCustodyStatus.Sealed),
            CompletedChunkCount = completedChunkCount,
            FailedChunkCount = failedChunkCount,
            SlowestChunkMilliseconds = slowestChunkMilliseconds,
            Message = ResolveSp07Message(context, expected, latestSession, latestTranscript, latestDeletionReceipt),
        };

        foreach (var blocker in BuildSp07Blockers(
            context,
            expected,
            latestSession,
            latestTranscript,
            latestDeletionReceipt,
            acceptedBallotCount,
            ciphertextSlotCount,
            chunkCount,
            chunkPlanningFailureMessage))
        {
            view.Blockers.Add(blocker);
        }

        return view;
    }

    private static IReadOnlyList<ElectionSp07ReadinessBlockerView> BuildSp07Blockers(
        VerificationPackageContext context,
        bool expected,
        ElectionPublicationProofSessionRecord? latestSession,
        ElectionPublicationProofTranscriptRecord? latestTranscript,
        ElectionPublicationWitnessDeletionReceiptRecord? latestDeletionReceipt,
        int acceptedBallotCount,
        int ciphertextSlotCount,
        int chunkCount,
        string? chunkPlanningFailureMessage)
    {
        if (!expected)
        {
            return Array.Empty<ElectionSp07ReadinessBlockerView>();
        }

        var blockers = new List<ElectionSp07ReadinessBlockerView>();
        if (acceptedBallotCount > ElectionSp07ProfileIds.HighAssuranceV1MaxAcceptedBallots)
        {
            blockers.Add(new ElectionSp07ReadinessBlockerView
            {
                Code = VerificationResultCodes.PublicationProofEnvelopeExceeded,
                Message = $"SP-07 high-assurance v1 supports up to {ElectionSp07ProfileIds.HighAssuranceV1MaxAcceptedBallots} accepted ballots.",
                BlocksOpen = true,
                BlocksFinalization = true,
            });
        }

        if (ciphertextSlotCount > ElectionSp07ProfileIds.HighAssuranceV1MaxEncryptedSlots)
        {
            blockers.Add(new ElectionSp07ReadinessBlockerView
            {
                Code = VerificationResultCodes.PublicationProofEnvelopeExceeded,
                Message = $"SP-07 high-assurance v1 supports up to {ElectionSp07ProfileIds.HighAssuranceV1MaxEncryptedSlots} encrypted ballot slots.",
                BlocksOpen = true,
                BlocksFinalization = true,
            });
        }

        if (!string.IsNullOrWhiteSpace(chunkPlanningFailureMessage))
        {
            blockers.Add(new ElectionSp07ReadinessBlockerView
            {
                Code = VerificationResultCodes.PublicationProofEnvelopeExceeded,
                Message = chunkPlanningFailureMessage,
                BlocksOpen = true,
                BlocksFinalization = true,
            });
        }

        if (chunkCount > ElectionSp07ProfileIds.HighAssuranceV1MaxPublicationChunks)
        {
            blockers.Add(new ElectionSp07ReadinessBlockerView
            {
                Code = VerificationResultCodes.PublicationProofEnvelopeExceeded,
                Message = $"SP-07 high-assurance v1 supports up to {ElectionSp07ProfileIds.HighAssuranceV1MaxPublicationChunks} publication chunks.",
                BlocksOpen = false,
                BlocksFinalization = true,
            });
        }

        if (latestSession?.Status == ElectionPublicationProofSessionStatus.Failed)
        {
            blockers.Add(new ElectionSp07ReadinessBlockerView
            {
                Code = latestSession.FailureCode ?? VerificationResultCodes.PublicationProofVerificationFailed,
                Message = latestSession.FailureReason ?? "SP-07 publication proof generation or self-verification failed.",
                BlocksOpen = false,
                BlocksFinalization = true,
            });
        }

        if (context.Election.LifecycleState == ElectionLifecycleState.Finalized && latestTranscript is null)
        {
            blockers.Add(new ElectionSp07ReadinessBlockerView
            {
                Code = VerificationResultCodes.PublicationProofTranscriptMissing,
                Message = "A finalized high-assurance election requires a public SP-07 publication-proof transcript.",
                BlocksOpen = false,
                BlocksFinalization = true,
            });
        }

        if (latestTranscript is not null &&
            latestDeletionReceipt?.DeletionStatus != ElectionPublicationWitnessDeletionStatus.Completed)
        {
            blockers.Add(new ElectionSp07ReadinessBlockerView
            {
                Code = VerificationResultCodes.PublicationProofWitnessDeletionMissing,
                Message = "A verified SP-07 proof requires a witness deletion receipt after successful self-verification.",
                BlocksOpen = false,
                BlocksFinalization = true,
            });
        }

        return blockers;
    }

    private static Sp07PublicationChunkPlan? TryCreateSp07ChunkPlan(
        int acceptedBallotCount,
        int ciphertextSlotCount,
        out string? failureMessage)
    {
        failureMessage = null;
        if (acceptedBallotCount < 1)
        {
            return null;
        }

        if (acceptedBallotCount > ElectionSp07ProfileIds.HighAssuranceV1MaxAcceptedBallots ||
            ciphertextSlotCount > ElectionSp07ProfileIds.HighAssuranceV1MaxEncryptedSlots)
        {
            return null;
        }

        var options = new Sp07PublicationChunkPlannerOptions(
            MaxBallotsPerChunk: Math.Max(
                1,
                (int)Math.Ceiling(
                    (double)ElectionSp07ProfileIds.HighAssuranceV1MaxAcceptedBallots /
                    ElectionSp07ProfileIds.HighAssuranceV1MaxPublicationChunks)),
            MinBallotsPerChunk: 2,
            MaxChunks: ElectionSp07ProfileIds.HighAssuranceV1MaxPublicationChunks,
            MaxEncryptedSlots: ElectionSp07ProfileIds.HighAssuranceV1MaxEncryptedSlots);

        try
        {
            return new Sp07PublicationChunkPlanner(options)
                .CreatePlan(acceptedBallotCount, ciphertextSlotCount);
        }
        catch (Sp07PublicationProofException ex)
        {
            failureMessage = ex.Message;
            return null;
        }
    }

    private static string ResolveSp07ResultCode(
        bool expected,
        ElectionPublicationProofSessionRecord? latestSession,
        ElectionPublicationProofTranscriptRecord? latestTranscript,
        ElectionPublicationWitnessDeletionReceiptRecord? latestDeletionReceipt)
    {
        if (!expected)
        {
            return VerificationResultCodes.PackageStructureValid;
        }

        if (latestSession?.Status == ElectionPublicationProofSessionStatus.Failed)
        {
            return latestSession.FailureCode ?? VerificationResultCodes.PublicationProofVerificationFailed;
        }

        if (latestTranscript is null)
        {
            return VerificationResultCodes.PublicationProofEvidencePending;
        }

        if (latestDeletionReceipt?.DeletionStatus != ElectionPublicationWitnessDeletionStatus.Completed)
        {
            return VerificationResultCodes.PublicationProofWitnessDeletionMissing;
        }

        return VerificationResultCodes.PublicationProofEvidenceValid;
    }

    private static ElectionSp07PublicationProofManifestArtifactRecord? TryReadSp07Manifest(
        ElectionPublicationProofTranscriptRecord? transcript)
    {
        if (transcript is null ||
            string.IsNullOrWhiteSpace(transcript.ProofBytes) ||
            !transcript.ProofBytes.Contains(
                ElectionSp07PublicationProofManifestArtifactRecord.SchemaVersion,
                StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ElectionSp07PublicationProofManifestArtifactRecord>(
                transcript.ProofBytes,
                VerificationJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int ResolveCompletedSp07ChunkCount(
        ElectionPublicationProofSessionRecord? latestSession,
        int chunkCount) =>
        latestSession?.Status is ElectionPublicationProofSessionStatus.Verified or
            ElectionPublicationProofSessionStatus.WitnessDeleted
            ? chunkCount
            : 0;

    private static int ResolveFailedSp07ChunkCount(
        ElectionPublicationProofSessionRecord? latestSession,
        int chunkCount) =>
        latestSession?.Status == ElectionPublicationProofSessionStatus.Failed && chunkCount > 0
            ? 1
            : 0;

    private static ElectionClosedProgressStatusProto ResolveSp07ProgressStatus(
        ElectionRecord election,
        ElectionPublicationProofSessionRecord? latestSession) =>
        latestSession?.Status switch
        {
            ElectionPublicationProofSessionStatus.Pending =>
                ElectionClosedProgressStatusProto.ClosedProgressPublicationProofPending,
            ElectionPublicationProofSessionStatus.Generating =>
                ElectionClosedProgressStatusProto.ClosedProgressPublicationProofGenerating,
            ElectionPublicationProofSessionStatus.SelfVerifying =>
                ElectionClosedProgressStatusProto.ClosedProgressPublicationProofSelfVerifying,
            ElectionPublicationProofSessionStatus.Failed =>
                ElectionClosedProgressStatusProto.ClosedProgressPublicationProofFailed,
            ElectionPublicationProofSessionStatus.Verified or
                ElectionPublicationProofSessionStatus.WitnessDeleted =>
                ElectionClosedProgressStatusProto.ClosedProgressPublicationProofVerified,
            _ => (ElectionClosedProgressStatusProto)(int)election.ClosedProgressStatus,
        };

    private static string ResolveSp07Message(
        VerificationPackageContext context,
        bool expected,
        ElectionPublicationProofSessionRecord? latestSession,
        ElectionPublicationProofTranscriptRecord? latestTranscript,
        ElectionPublicationWitnessDeletionReceiptRecord? latestDeletionReceipt)
    {
        if (!context.CanViewPackageStatus)
        {
            return "SP-07 publication-proof status is not visible to this actor.";
        }

        if (!expected)
        {
            return "SP-07 high-assurance publication proof is not claimed by this election profile.";
        }

        if (latestSession?.Status == ElectionPublicationProofSessionStatus.Failed)
        {
            return latestSession.FailureReason ?? "SP-07 publication proof generation or self-verification failed.";
        }

        if (latestTranscript is null)
        {
            return "SP-07 publication-proof evidence is pending.";
        }

        if (latestDeletionReceipt?.DeletionStatus != ElectionPublicationWitnessDeletionStatus.Completed)
        {
            return "SP-07 proof transcript is available, but witness deletion receipt is not complete.";
        }

        return "SP-07 publication-proof evidence is available for verification package export.";
    }

    private static bool IsSp07EvidenceExpected(ElectionRecord election) =>
        !election.SelectedProfileDevOnly &&
        (string.Equals(election.SelectedProfileId, ElectionSelectableProfileCatalog.TrusteeProductionProfileId, StringComparison.Ordinal) ||
         string.Equals(election.SelectedProfileId, ElectionSelectableProfileCatalog.AdminOnlyProductionProfileId, StringComparison.Ordinal) ||
         string.Equals(election.ControlDomainProfileId, ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1, StringComparison.Ordinal));

    private static string ComputeAcceptedBallotSetHash(
        IReadOnlyList<ElectionAcceptedBallotRecord> acceptedBallots) =>
        acceptedBallots.Count == 0
            ? string.Empty
            : VerificationCanonicalHash.ToLowerHex(
                VerificationCanonicalHash.ComputeAcceptedBallotInventoryHash(acceptedBallots));

    private static string ComputePublishedBallotStreamHash(
        IReadOnlyList<ElectionPublishedBallotRecord> publishedBallots) =>
        publishedBallots.Count == 0
            ? string.Empty
            : VerificationCanonicalHash.ToLowerHex(
                VerificationCanonicalHash.ComputePublishedBallotStreamHash(publishedBallots));

    private static string ComputeWitnessDeletionReceiptHash(
        ElectionPublicationWitnessDeletionReceiptRecord receipt) =>
        VerificationCanonicalHash.ComputeSha256UpperHex(
            $"{receipt.ElectionId}|{receipt.ProofSessionId:N}|{receipt.WitnessSetId:N}|{receipt.WitnessSetHash}|{receipt.WitnessCount}|{receipt.TranscriptHash}|{receipt.ProofHash}|{receipt.DeletionStatus}|{receipt.DeletedAt:O}");

    private static ElectionSp04EvidenceStatusView BuildSp04EvidenceStatus(VerificationPackageContext context)
    {
        var receiptCommitments = BuildSp04ReceiptCommitments(context.AcceptedBallots);
        var hasSealedBallotDefinition = HasSealedBallotDefinition(context);
        var packageReady =
            context.Election.LifecycleState == ElectionLifecycleState.Finalized &&
            context.LatestReportPackage?.Status == ElectionReportPackageStatus.Sealed &&
            context.ProtocolPackageBinding?.Status == ProtocolPackageBindingStatus.Sealed &&
            hasSealedBallotDefinition;
        var message = !context.CanViewPackageStatus
            ? "SP-04 evidence status is not visible to this actor."
            : !hasSealedBallotDefinition
                ? "SP-04 evidence is waiting for a sealed ballot definition."
                : packageReady
                    ? "SP-04 evidence is available for verification package export."
                    : "SP-04 evidence is being collected and becomes exportable after finalization.";

        return new ElectionSp04EvidenceStatusView
        {
            EvidenceExpected = true,
            PublicEvidenceAvailable = packageReady,
            RestrictedEvidenceAvailable = packageReady && context.CanExportRestrictedPackage,
            PreparedPackageCount = context.PreparedBallotCommitments.Count,
            SpoiledPackageCount = context.SpoiledPreparedBallots.Count,
            AcceptedBoundReceiptCount = receiptCommitments.Count,
            ReceiptCommitmentSetHash = ComputeReceiptCommitmentSetHash(receiptCommitments),
            Message = message,
        };
    }

    private static ElectionSp05EvidenceStatusView BuildSp05EvidenceStatus(VerificationPackageContext context)
    {
        var latestImportEvidence = context.RosterImportEvidences
            .OrderByDescending(x => x.RosterImportVersion)
            .ThenByDescending(x => x.ImportedAt)
            .FirstOrDefault();
        var activeDenominatorEntries = context.Election.EligibilityMutationPolicy == EligibilityMutationPolicy.FrozenAtOpen
            ? context.RosterEntries.Where(x => x.WasPresentAtOpen && x.WasActiveAtOpen).ToArray()
            : context.RosterEntries.Where(x => x.WasPresentAtOpen && x.IsActive).ToArray();
        var countedParticipationCount = context.ParticipationRecords.Count(x =>
            x.ParticipationStatus == ElectionParticipationStatus.CountedAsVoted);
        var packageReady =
            context.Election.LifecycleState == ElectionLifecycleState.Finalized &&
            context.LatestReportPackage?.Status == ElectionReportPackageStatus.Sealed &&
            context.ProtocolPackageBinding?.Status == ProtocolPackageBindingStatus.Sealed &&
            HasSealedBallotDefinition(context);
        var providerReady =
            context.Election.ContactCodeProviderReadiness == ElectionContactCodeProviderReadiness.Ready;
        var latestResultCode = providerReady
            ? VerificationResultCodes.EligibilityEvidenceValid
            : VerificationResultCodes.EligibilityDevOnlyVerificationBlocked;
        var message = !context.CanViewPackageStatus
            ? "SP-05 eligibility evidence status is not visible to this actor."
            : latestImportEvidence is null
                ? "SP-05 eligibility evidence is waiting for roster import evidence."
                : packageReady
                    ? "SP-05 eligibility evidence is available for verification package export."
                    : "SP-05 eligibility evidence is being collected and becomes exportable after finalization.";

        return new ElectionSp05EvidenceStatusView
        {
            EvidenceExpected = true,
            PublicEvidenceAvailable = packageReady,
            RestrictedEvidenceAvailable = packageReady && context.CanExportRestrictedPackage,
            RosteredCount = context.RosterEntries.Count,
            LinkedCount = context.RosterEntries.Count(x => x.IsLinked),
            ActiveDenominatorCount = activeDenominatorEntries.Length,
            CommitmentCount = context.CommitmentRegistrations.Count,
            CountedParticipationCount = countedParticipationCount,
            DuplicateContactWarningCount = latestImportEvidence?.DuplicateContactWarningCount ?? 0,
            RosterCanonicalHash = latestImportEvidence?.RosterCanonicalHash ??
                ElectionEligibilityContracts.ComputeRosterCanonicalHash(context.RosterEntries),
            CommitmentTreeRoot = ComputeCommitmentRootHash(context.CommitmentRegistrations),
            LatestEliResultCode = latestResultCode,
            Message = message,
        };
    }

    private static bool IsSp06EvidenceExpected(ElectionRecord election) =>
        string.Equals(
            election.ControlDomainProfileId,
            ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1,
            StringComparison.Ordinal);

    private ElectionSp08ReleaseIntegrityStatusView BuildSp08ReleaseIntegrityStatus(
        VerificationPackageContext context,
        bool publicPackageAvailable,
        bool restrictedPackageAvailable,
        bool includePackageHashes)
    {
        var officialRequired = IsSp08OfficialEvidenceRequired(context.Election);
        var view = new ElectionSp08ReleaseIntegrityStatusView
        {
            EvidenceExpected = true,
            PublicEvidenceAvailable = false,
            RestrictedEvidenceAvailable = false,
            EvidenceMode = ElectionSp08ProfileIds.EvidenceModeDevelopmentPlaceholder,
            NotForReleaseIntegrityClaims = true,
            BlocksHighAssurance = officialRequired,
            ReleaseManifestName = ElectionSp08ProfileIds.ReleaseManifestFileName,
            ReleaseManifestHash = string.Empty,
            ProtocolPackageManifestName = ProtocolPackagePromotionService.ReleaseManifestFileName,
            ProtocolPackageManifestHash = context.ProtocolPackageBinding?.ReleaseManifestHash ?? string.Empty,
            PrimaryResultCode = VerificationResultCodes.ReleaseIntegrityManifestMissing,
            PrimaryIssue = officialRequired
                ? "Official SP-08 release evidence is required before high-assurance claims can be made."
                : "SP-08 release evidence is not available yet.",
            Message = !context.CanViewPackageStatus
                ? "SP-08 release-integrity status is not visible to this actor."
                : "SP-08 release-integrity evidence is not exportable yet.",
        };

        if (!context.CanViewPackageStatus)
        {
            return view;
        }

        if (!publicPackageAvailable)
        {
            return view;
        }

        if (!includePackageHashes)
        {
            view.PrimaryResultCode = VerificationResultCodes.ReleaseIntegrityEvidencePending;
            view.Message = "SP-08 release-integrity details are available from the verification package status endpoint.";
            return view;
        }

        var export = _verificationPackageExportService.Export(
            BuildExportRequest(context, VerificationPackageView.PublicAnonymous));
        if (!export.Success)
        {
            view.PrimaryResultCode = export.Code;
            view.PrimaryIssue = export.Message;
            view.Message = "SP-08 release-integrity evidence could not be read from the public package export.";
            return view;
        }

        var filesByPath = export.Files
            .GroupBy(x => x.RelativePath, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        foreach (var relativePath in Sp08EvidenceFileNames)
        {
            view.EvidenceFiles.Add(BuildSp08EvidenceFileStatus(filesByPath, relativePath));
        }

        view.EvidenceFileCount = view.EvidenceFiles.Count(x => x.IsPresent);
        var releaseManifest = TryReadSp08Artifact<ElectionSp08ReleaseManifestArtifactRecord>(
            filesByPath,
            VerificationPackageFileNames.Sp08ReleaseManifest);
        var releaseIntegrity = TryReadSp08Artifact<ElectionSp08ReleaseIntegrityArtifactRecord>(
            filesByPath,
            VerificationPackageFileNames.Sp08ReleaseIntegrity);
        var verifierOutput = TryReadSp08Artifact<ElectionSp08VerifierOutputArtifactRecord>(
            filesByPath,
            VerificationPackageFileNames.Sp08ReleaseIntegrityVerifierOutput);

        if (releaseIntegrity is null || releaseManifest is null)
        {
            view.PrimaryIssue = "SP-08 release manifest or release-integrity artifact is missing or malformed.";
            view.Message = view.PrimaryIssue;
            return view;
        }

        var components = releaseManifest.Components.Count > 0
            ? releaseManifest.Components
            : releaseIntegrity.Components;
        var lifecycleBindings = releaseManifest.LifecycleBindings.Count > 0
            ? releaseManifest.LifecycleBindings
            : releaseIntegrity.LifecycleBindings;
        var primaryIssue = ResolveSp08PrimaryIssue(verifierOutput) ??
            ResolveSp08LifecycleIssue(lifecycleBindings) ??
            (releaseIntegrity.BlocksHighAssurance
                ? "SP-08 release evidence is present but cannot satisfy high-assurance release-integrity claims."
                : string.Empty);

        view.PublicEvidenceAvailable = true;
        view.RestrictedEvidenceAvailable = restrictedPackageAvailable && context.CanExportRestrictedPackage;
        view.EvidenceMode = releaseIntegrity.EvidenceMode;
        view.NotForReleaseIntegrityClaims = releaseIntegrity.NotForReleaseIntegrityClaims;
        view.BlocksHighAssurance = releaseIntegrity.BlocksHighAssurance ||
            (officialRequired && !ElectionSp08ReleaseIntegrityRules.IsOfficialEvidenceMode(releaseIntegrity.EvidenceMode)) ||
            lifecycleBindings.Any(x => !x.MatchesSealedPolicy);
        view.ReleaseManifestName = releaseIntegrity.ReleaseManifestName;
        view.ReleaseManifestHash = releaseIntegrity.ReleaseManifestHash;
        view.ProtocolPackageManifestName = releaseIntegrity.ProtocolPackageManifestName;
        view.ProtocolPackageManifestHash = releaseIntegrity.ProtocolPackageManifestHash;
        view.PrimaryResultCode = releaseIntegrity.PrimaryResultCode;
        view.PrimaryIssue = primaryIssue;
        view.ComponentCount = components.Count;
        view.LifecycleBindingCount = lifecycleBindings.Count;
        view.MobileEvidenceIncluded = components.Any(x =>
            string.Equals(x.ComponentId, ElectionSp08ProfileIds.MobileAppComponent, StringComparison.Ordinal));
        view.Message = ResolveSp08StatusMessage(view, officialRequired);

        foreach (var component in components)
        {
            view.Components.Add(new ElectionSp08ReleaseComponentStatusView
            {
                ComponentId = component.ComponentId,
                ComponentType = component.ComponentType,
                EvidenceMode = component.EvidenceMode,
                ArtifactName = component.ArtifactName,
                ArtifactDigest = component.ArtifactDigest,
                ImmutableReference = component.ImmutableReference,
                BuildWorkflowRunId = component.BuildWorkflowRunId ?? string.Empty,
                DistributionReference = component.DistributionReference ?? string.Empty,
                HasSigningFingerprint = !string.IsNullOrWhiteSpace(component.SigningFingerprint),
                IsPlaceholder = component.IsPlaceholder,
            });
        }

        foreach (var lifecycleBinding in lifecycleBindings)
        {
            view.LifecycleBindings.Add(new ElectionSp08LifecycleBindingStatusView
            {
                LifecycleStage = lifecycleBinding.LifecycleStage,
                ExpectedReleaseId = lifecycleBinding.ExpectedReleaseId,
                ObservedReleaseId = lifecycleBinding.ObservedReleaseId,
                ExpectedArtifactDigest = lifecycleBinding.ExpectedArtifactDigest,
                ObservedArtifactDigest = lifecycleBinding.ObservedArtifactDigest,
                MatchesSealedPolicy = lifecycleBinding.MatchesSealedPolicy,
            });
        }

        return view;
    }

    private static ElectionSp08EvidenceFileStatusView BuildSp08EvidenceFileStatus(
        IReadOnlyDictionary<string, ElectionVerificationPackageFile> filesByPath,
        string relativePath)
    {
        if (!filesByPath.TryGetValue(relativePath, out var file))
        {
            return new ElectionSp08EvidenceFileStatusView
            {
                RelativePath = relativePath,
                Visibility = ElectionVerificationArtifactVisibilityProto.VerificationArtifactPublic,
                IsPresent = false,
                ContentHash = string.Empty,
            };
        }

        return new ElectionSp08EvidenceFileStatusView
        {
            RelativePath = file.RelativePath,
            Visibility = file.Visibility.ToProto(),
            IsPresent = true,
            ContentHash = $"sha256:{VerificationCanonicalHash.ComputeManifestFileSha256(file.Content)}",
        };
    }

    private static T? TryReadSp08Artifact<T>(
        IReadOnlyDictionary<string, ElectionVerificationPackageFile> filesByPath,
        string relativePath)
    {
        if (!filesByPath.TryGetValue(relativePath, out var file))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(file.ContentText, VerificationJson.Options);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string? ResolveSp08PrimaryIssue(ElectionSp08VerifierOutputArtifactRecord? verifierOutput) =>
        verifierOutput?.Results
            .FirstOrDefault(x => x.Status is VerificationCheckStatus.Fail or VerificationCheckStatus.Warn)
            ?.Message;

    private static string? ResolveSp08LifecycleIssue(
        IReadOnlyList<ElectionSp08LifecycleReleaseBindingRecord> lifecycleBindings) =>
        lifecycleBindings.Any(x => !x.MatchesSealedPolicy)
            ? "One or more SP-08 lifecycle release bindings do not match the sealed policy."
            : null;

    private static string ResolveSp08StatusMessage(
        ElectionSp08ReleaseIntegrityStatusView view,
        bool officialRequired)
    {
        if (!view.PublicEvidenceAvailable)
        {
            return "SP-08 release-integrity evidence is not exportable yet.";
        }

        if (view.BlocksHighAssurance && officialRequired)
        {
            return "SP-08 release-integrity evidence blocks high-assurance release claims.";
        }

        if (ElectionSp08ReleaseIntegrityRules.IsOfficialEvidenceMode(view.EvidenceMode) &&
            !view.NotForReleaseIntegrityClaims)
        {
            return "Official SP-08 release-integrity evidence is present in the verification package.";
        }

        return "Development placeholder SP-08 release-integrity evidence is present and is not official release evidence.";
    }

    private static bool IsSp08OfficialEvidenceRequired(ElectionRecord election) =>
        IsSp07EvidenceExpected(election);

    private ElectionSp09ExternalReviewStatusView BuildSp09ExternalReviewStatus(
        VerificationPackageContext context,
        bool publicPackageAvailable,
        bool restrictedPackageAvailable,
        bool includePackageHashes)
    {
        var view = CreateDefaultSp09ExternalReviewStatus(context);
        if (!context.CanViewPackageStatus)
        {
            return new ElectionSp09ExternalReviewStatusView
            {
                EvidenceExpected = false,
                PublicEvidenceAvailable = false,
                RestrictedEvidenceAvailable = false,
                PrimaryResultCode = string.Empty,
                Message = "SP-09 external-review status is not visible to this actor.",
            };
        }

        if (!publicPackageAvailable)
        {
            view.Message = "SP-09 external-review evidence becomes exportable with the public verification package.";
            return view;
        }

        if (!includePackageHashes)
        {
            view.PublicEvidenceAvailable = true;
            view.PrimaryResultCode = VerificationResultCodes.ExternalReviewNotComplete;
            view.Message = "SP-09 external-review details are available from the verification package status endpoint.";
            return view;
        }

        var export = _verificationPackageExportService.Export(
            BuildExportRequest(context, VerificationPackageView.PublicAnonymous));
        if (!export.Success)
        {
            view.PrimaryResultCode = export.Code;
            view.PrimaryIssue = export.Message;
            view.Message = "SP-09 external-review evidence could not be read from the public package export.";
            return view;
        }

        var filesByPath = export.Files
            .GroupBy(x => x.RelativePath, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        foreach (var relativePath in Sp09PublicEvidenceFileNames)
        {
            view.EvidenceFiles.Add(BuildSp09EvidenceFileStatus(filesByPath, relativePath));
        }

        var status = TryReadSp09Artifact<ElectionSp09ExternalReviewStatusArtifactRecord>(
            filesByPath,
            VerificationPackageFileNames.Sp09ExternalReviewStatus);
        var verifierOutput = TryReadSp09Artifact<ElectionSp09VerifierOutputArtifactRecord>(
            filesByPath,
            VerificationPackageFileNames.Sp09ExternalReviewVerifierOutput);

        if (status is null)
        {
            view.PrimaryResultCode = VerificationResultCodes.ExternalReviewProgramMissing;
            view.PrimaryIssue = "SP-09 external-review status artifact is missing or malformed.";
            view.Message = view.PrimaryIssue;
            return view;
        }

        var primaryVerifierResult = ResolveSp09PrimaryVerifierResult(verifierOutput);
        view.PublicEvidenceAvailable = true;
        view.RestrictedEvidenceAvailable = restrictedPackageAvailable && context.CanExportRestrictedPackage;
        view.ProgramVersion = status.ProgramVersion;
        view.ReviewScope = status.ReviewScope;
        view.ReviewType = status.ReviewType;
        view.ReviewPhase = status.ReviewPhase;
        view.DetailedStatus = status.DetailedStatus;
        view.Availability = status.Availability;
        view.ClaimState = status.ClaimState;
        view.ReviewScopeMatchesElection = status.ReviewScopeMatchesElection;
        view.PrimaryResultCode = primaryVerifierResult?.ResultCode ?? status.PrimaryResultCode;
        view.PrimaryIssue = primaryVerifierResult?.Message ?? status.PrimaryIssue ?? string.Empty;
        view.CustomerSafeSummaryHash = status.CustomerSafeSummaryHash ?? string.Empty;
        view.CustomerSafeSummaryUrl = status.CustomerSafeSummaryUrl ?? string.Empty;
        view.KnownLimitationsVersion = status.KnownLimitationsVersion ?? string.Empty;
        view.KnownLimitationsHash = status.KnownLimitationsHash ?? string.Empty;
        view.ReviewedArtifactCount = status.ReviewedArtifacts.Count;
        view.PublicEvidenceFileCount = status.PublicEvidenceFiles.Count;
        view.RestrictedEvidenceFileCount = view.RestrictedEvidenceAvailable
            ? Sp09RestrictedEvidenceFileNames.Length
            : 0;
        view.OpenCriticalFindingCount = CountSp09OpenFindings(status.FindingSummary, "critical");
        view.OpenHighFindingCount = CountSp09OpenFindings(status.FindingSummary, "high");
        view.OpenFindingCount = status.FindingSummary.Sum(x => x.OpenCount);
        view.RequiresRedesign = string.Equals(
            status.DetailedStatus,
            ElectionSp09ProfileIds.StatusRequiresRedesign,
            StringComparison.Ordinal);
        view.BlocksReviewedClaims =
            view.RequiresRedesign ||
            ElectionSp09ExternalReviewRules.HasBlockingOpenFindings(status.FindingSummary) ||
            primaryVerifierResult?.Status == VerificationCheckStatus.Fail;
        view.Message = ResolveSp09StatusMessage(view, status);

        foreach (var artifact in status.ReviewedArtifacts)
        {
            view.ReviewedArtifacts.Add(new ElectionSp09ReviewedArtifactStatusView
            {
                ArtifactId = artifact.ArtifactId,
                ArtifactType = artifact.ArtifactType,
                ArtifactName = artifact.ArtifactName,
                ArtifactHash = artifact.ArtifactHash,
                ArtifactVersion = artifact.ArtifactVersion ?? string.Empty,
                ReviewScope = artifact.ReviewScope,
            });
        }

        foreach (var finding in status.FindingSummary)
        {
            view.FindingSummary.Add(new ElectionSp09FindingSeverityStatusView
            {
                Severity = finding.Severity,
                OpenCount = finding.OpenCount,
                FixedCount = finding.FixedCount,
                AcceptedLimitationCount = finding.AcceptedLimitationCount,
            });
        }

        if (view.RestrictedEvidenceAvailable)
        {
            var restrictedExport = _verificationPackageExportService.Export(
                BuildExportRequest(context, VerificationPackageView.RestrictedOwnerAuditor));
            if (restrictedExport.Success)
            {
                var restrictedFilesByPath = restrictedExport.Files
                    .GroupBy(x => x.RelativePath, StringComparer.Ordinal)
                    .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
                foreach (var relativePath in Sp09RestrictedEvidenceFileNames)
                {
                    view.EvidenceFiles.Add(BuildSp09EvidenceFileStatus(restrictedFilesByPath, relativePath));
                }

                view.RestrictedEvidenceFileCount = view.EvidenceFiles.Count(x =>
                    x.Visibility == ElectionVerificationArtifactVisibilityProto.VerificationArtifactRestricted &&
                    x.IsPresent);
            }
        }

        return view;
    }

    private static ElectionSp09ExternalReviewStatusView CreateDefaultSp09ExternalReviewStatus(
        VerificationPackageContext context)
    {
        var legacyStatus = context.ProtocolPackageBinding?.ExternalReviewStatus ??
            ProtocolPackageExternalReviewStatus.NotReviewed;
        var summary = ElectionSp09ExternalReviewRules.BuildCustomerSafeSummary(legacyStatus);
        return new ElectionSp09ExternalReviewStatusView
        {
            EvidenceExpected = true,
            PublicEvidenceAvailable = false,
            RestrictedEvidenceAvailable = false,
            ProgramVersion = ElectionSp09ProfileIds.ExternalExaminationProgramVersion,
            ReviewScope = ElectionSp09ProfileIds.ReviewScopeProtocolOmegaV1,
            ReviewType = ElectionSp09ProfileIds.ReviewTypeCryptographicSecurity,
            ReviewPhase = ElectionSp09ProfileIds.ReviewPhaseProtocolProofP1,
            DetailedStatus = ElectionSp09ProfileIds.StatusNotStarted,
            Availability = summary.Availability,
            ClaimState = summary.ClaimState,
            ReviewScopeMatchesElection = context.ProtocolPackageBinding is not null,
            PrimaryResultCode = VerificationResultCodes.ExternalReviewNotComplete,
            PrimaryIssue = summary.Wording,
            Message = summary.Wording,
        };
    }

    private static ElectionSp09EvidenceFileStatusView BuildSp09EvidenceFileStatus(
        IReadOnlyDictionary<string, ElectionVerificationPackageFile> filesByPath,
        string relativePath)
    {
        if (!filesByPath.TryGetValue(relativePath, out var file))
        {
            return new ElectionSp09EvidenceFileStatusView
            {
                RelativePath = relativePath,
                Visibility = VerificationPrivacyBoundary.IsRestrictedArtifactPath(relativePath)
                    ? ElectionVerificationArtifactVisibilityProto.VerificationArtifactRestricted
                    : ElectionVerificationArtifactVisibilityProto.VerificationArtifactPublic,
                IsPresent = false,
                ContentHash = string.Empty,
            };
        }

        return new ElectionSp09EvidenceFileStatusView
        {
            RelativePath = file.RelativePath,
            Visibility = file.Visibility.ToProto(),
            IsPresent = true,
            ContentHash = $"sha256:{VerificationCanonicalHash.ComputeManifestFileSha256(file.Content)}",
        };
    }

    private static T? TryReadSp09Artifact<T>(
        IReadOnlyDictionary<string, ElectionVerificationPackageFile> filesByPath,
        string relativePath)
    {
        if (!filesByPath.TryGetValue(relativePath, out var file))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(file.ContentText, VerificationJson.Options);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static VerifierCheckResultRecord? ResolveSp09PrimaryVerifierResult(
        ElectionSp09VerifierOutputArtifactRecord? verifierOutput) =>
        verifierOutput?.Results.FirstOrDefault(x => x.Status == VerificationCheckStatus.Fail) ??
        verifierOutput?.Results.FirstOrDefault(x => x.Status == VerificationCheckStatus.Warn);

    private static int CountSp09OpenFindings(
        IReadOnlyList<ElectionSp09FindingSeverityCountRecord> findingSummary,
        string severity) =>
        findingSummary
            .Where(x => string.Equals(x.Severity, severity, StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.OpenCount);

    private static string ResolveSp09StatusMessage(
        ElectionSp09ExternalReviewStatusView view,
        ElectionSp09ExternalReviewStatusArtifactRecord status)
    {
        if (view.RequiresRedesign)
        {
            return "External review requires redesign for the declared scope; reviewed-scope claims are blocked.";
        }

        if (view.BlocksReviewedClaims && !string.IsNullOrWhiteSpace(view.PrimaryIssue))
        {
            return view.PrimaryIssue;
        }

        if (string.Equals(status.Availability, ElectionSp09ProfileIds.AvailabilityAvailable, StringComparison.Ordinal))
        {
            return ElectionSp09ExternalReviewRules.GetAllowedWordingForClaimState(status.ClaimState);
        }

        return status.PrimaryIssue ??
            ElectionSp09ExternalReviewRules.GetAllowedWordingForClaimState(status.ClaimState);
    }

    private ElectionVerificationPackageExportAvailabilityView BuildPackageAvailability(
        VerificationPackageContext context,
        VerificationPackageView packageView,
        bool includePackageHash)
    {
        var verifierProfileId = ResolveVerifierProfileId(packageView);
        var protoView = packageView.ToProto();
        var blocker = ResolveExportBlocker(context, packageView);
        if (blocker != ElectionVerificationPackageBlockerProto.VerificationPackageBlockerNone)
        {
            return new ElectionVerificationPackageExportAvailabilityView
            {
                PackageView = protoView,
                VerifierProfileId = verifierProfileId,
                IsAvailable = false,
                Blocker = blocker,
                BlockerCode = ResolveBlockerCode(blocker, context),
                Message = ResolveBlockerMessage(blocker, packageView, context),
                CanRetry = CanRetryPackageExport(blocker, context),
            };
        }

        var packageId = string.Empty;
        var packageHash = string.Empty;
        if (includePackageHash)
        {
            var result = _verificationPackageExportService.Export(BuildExportRequest(context, packageView));
            if (!result.Success)
            {
                return new ElectionVerificationPackageExportAvailabilityView
                {
                    PackageView = protoView,
                    VerifierProfileId = verifierProfileId,
                    IsAvailable = false,
                    Blocker = MapExportBlocker(result.Code),
                    BlockerCode = result.Code,
                    Message = result.Message,
                    CanRetry = CanRetryPackageExport(MapExportBlocker(result.Code), context),
                };
            }

            packageId = result.PackageId ?? string.Empty;
            packageHash = result.PackageHash ?? string.Empty;
        }

        return new ElectionVerificationPackageExportAvailabilityView
        {
            PackageView = protoView,
            VerifierProfileId = verifierProfileId,
            IsAvailable = true,
            Blocker = ElectionVerificationPackageBlockerProto.VerificationPackageBlockerNone,
            BlockerCode = string.Empty,
            Message = packageView == VerificationPackageView.RestrictedOwnerAuditor
                ? "Restricted owner/auditor verification package export is available."
                : "Public anonymous verification package export is available.",
            PackageId = packageId,
            PackageHash = packageHash,
            CanRetry = false,
        };
    }

    private ElectionVerificationPackageExportRequest BuildExportRequest(
        VerificationPackageContext context,
        VerificationPackageView packageView) =>
        new(
            context.Election,
            context.ProtocolPackageBinding,
            context.LatestReportPackage,
            context.ReportArtifacts,
            context.BoundaryArtifacts,
            context.AcceptedBallots,
            context.PublishedBallots,
            context.FinalizationSessions,
            context.FinalizationShares,
            context.ReleaseEvidenceRecords,
            context.RosterEntries,
            context.ParticipationRecords,
            packageView,
            ResolveVerifierProfileId(packageView),
            context.CanExportRestrictedPackage,
            context.LatestReportPackage?.SealedAt ??
            context.LatestReportPackage?.AttemptedAt ??
            context.Election.FinalizedAt,
            context.VoterCeremonyRecords,
            context.PreparedBallotCommitments,
            context.SpoiledPreparedBallots,
            context.RosterImportEvidences,
            context.EligibilityPolicyEvidences,
            context.CommitmentSchemeEvidences,
            context.CommitmentRegistrations,
            context.CheckoffConsumptions,
            context.EligibilityActivationEvents,
            TrusteeControlDomainRecords: BuildSp06ControlDomainRecords(context),
            PublicationProofTranscripts: context.PublicationProofTranscripts,
            PublicationProofSessions: context.PublicationProofSessions,
            PublicationWitnessDeletionReceipts: context.PublicationWitnessDeletionReceipts);

    private static IReadOnlyList<ElectionTrusteeControlDomainRecord> BuildSp06ControlDomainRecords(
        VerificationPackageContext context)
    {
        var ceremonyVersion = ResolveSp06CeremonyVersion(context);
        var trusteeStates = ceremonyVersion is null
            ? Array.Empty<ElectionCeremonyTrusteeStateRecord>()
            : context.CeremonyTrusteeStates
                .Where(x => x.CeremonyVersionId == ceremonyVersion.Id)
                .ToArray();
        var shareCustodyRecords = ceremonyVersion is null
            ? Array.Empty<ElectionCeremonyShareCustodyRecord>()
            : context.CeremonyShareCustodyRecords
                .Where(x => x.CeremonyVersionId == ceremonyVersion.Id)
                .ToArray();

        return ElectionSp06ControlDomainMaterializer.BuildFromCeremonyEvidence(
            context.Election,
            ceremonyVersion,
            context.TrusteeInvitations,
            trusteeStates,
            shareCustodyRecords);
    }

    private static ElectionCeremonyVersionRecord? ResolveSp06CeremonyVersion(VerificationPackageContext context) =>
        context.CeremonyVersions
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.CompletedAt ?? x.StartedAt)
            .ThenByDescending(x => x.VersionNumber)
            .FirstOrDefault() ??
        context.CeremonyVersions
            .OrderByDescending(x => x.CompletedAt ?? x.StartedAt)
            .ThenByDescending(x => x.VersionNumber)
            .FirstOrDefault();

    private static ElectionCeremonyProfileRecord? BuildSp06ThresholdProfile(
        ElectionRecord election,
        ElectionCeremonyVersionRecord? ceremonyVersion)
    {
        if (ceremonyVersion is null)
        {
            return null;
        }

        var timestamp = ceremonyVersion.StartedAt;
        return new ElectionCeremonyProfileRecord(
            ceremonyVersion.ProfileId,
            ceremonyVersion.ProfileId,
            "Materialized ceremony profile used for SP-06 package/readiness projection.",
            "ceremony-version",
            ceremonyVersion.ProfileId,
            ceremonyVersion.TrusteeCount,
            ceremonyVersion.RequiredApprovalCount,
            election.SelectedProfileDevOnly,
            timestamp,
            ceremonyVersion.CompletedAt ?? timestamp);
    }

    private static IReadOnlyList<ElectionSp04ReceiptCommitmentRecord> BuildSp04ReceiptCommitments(
        IReadOnlyList<ElectionAcceptedBallotRecord> acceptedBallots) =>
        acceptedBallots
            .Where(x =>
                x.PreparedBallotId.HasValue &&
                !string.IsNullOrWhiteSpace(x.PreparedBallotHash) &&
                !string.IsNullOrWhiteSpace(x.ReceiptCommitment) &&
                !string.IsNullOrWhiteSpace(x.ReceiptCommitmentScheme))
            .OrderBy(x => x.AcceptedAt)
            .ThenBy(x => x.Id)
            .Select(x => new ElectionSp04ReceiptCommitmentRecord(
                x.Id,
                x.PreparedBallotId!.Value,
                x.PreparedBallotHash!,
                x.ReceiptCommitment!,
                x.ReceiptCommitmentScheme!,
                x.AcceptedAt))
            .ToArray();

    private static string ComputeReceiptCommitmentSetHash(
        IReadOnlyList<ElectionSp04ReceiptCommitmentRecord> receiptCommitments)
    {
        var payload = string.Join(
            '\n',
            receiptCommitments
                .OrderBy(x => x.AcceptedBallotId)
                .Select(x =>
                    $"{x.AcceptedBallotId:N}|{x.PreparedBallotId:N}|{x.PreparedBallotHash}|{x.ReceiptCommitment}|{x.ReceiptCommitmentScheme}|{x.AcceptedAt:O}"));

        return VerificationCanonicalHash.ComputeSha256UpperHex(payload);
    }

    private static string ComputeCommitmentRootHash(IReadOnlyList<ElectionCommitmentRegistrationRecord> commitments)
    {
        var payload = string.Join(
            '\n',
            commitments
                .OrderBy(x => x.CommitmentHash, StringComparer.Ordinal)
                .Select(x => x.CommitmentHash));

        return VerificationCanonicalHash.ComputeSha256UpperHex(payload);
    }

    private static ElectionVerificationPackageStatusProto ResolvePackageStatus(VerificationPackageContext context)
    {
        if (!context.CanViewPackageStatus)
        {
            return ElectionVerificationPackageStatusProto.VerificationPackageNotVisible;
        }

        if (context.Election.LifecycleState != ElectionLifecycleState.Finalized)
        {
            return ElectionVerificationPackageStatusProto.VerificationPackageNotFinalized;
        }

        if (context.LatestReportPackage?.Status == ElectionReportPackageStatus.GenerationFailed)
        {
            return ElectionVerificationPackageStatusProto.VerificationPackageExportFailed;
        }

        if (context.LatestReportPackage?.Status != ElectionReportPackageStatus.Sealed)
        {
            return ElectionVerificationPackageStatusProto.VerificationPackageMissing;
        }

        if (!HasSealedBallotDefinition(context))
        {
            return ElectionVerificationPackageStatusProto.VerificationPackageMissing;
        }

        if (context.ProtocolPackageBinding is null ||
            context.ProtocolPackageBinding.Status != ProtocolPackageBindingStatus.Sealed)
        {
            return ElectionVerificationPackageStatusProto.VerificationPackageProtocolRefsBlocked;
        }

        return ElectionVerificationPackageStatusProto.VerificationPackageReady;
    }

    private static ElectionVerificationPackageBlockerProto ResolveExportBlocker(
        VerificationPackageContext context,
        VerificationPackageView packageView)
    {
        if (!context.CanViewPackageStatus)
        {
            return ElectionVerificationPackageBlockerProto.VerificationPackageBlockerNotVisible;
        }

        if (packageView == VerificationPackageView.RestrictedOwnerAuditor &&
            !context.CanExportRestrictedPackage)
        {
            return ElectionVerificationPackageBlockerProto.VerificationPackageBlockerUnauthorized;
        }

        if (context.Election.LifecycleState != ElectionLifecycleState.Finalized)
        {
            return ElectionVerificationPackageBlockerProto.VerificationPackageBlockerNotFinalized;
        }

        if (context.LatestReportPackage?.Status == ElectionReportPackageStatus.GenerationFailed)
        {
            return ElectionVerificationPackageBlockerProto.VerificationPackageBlockerExportFailed;
        }

        if (context.LatestReportPackage?.Status != ElectionReportPackageStatus.Sealed)
        {
            return ElectionVerificationPackageBlockerProto.VerificationPackageBlockerMissingPackage;
        }

        if (!HasSealedBallotDefinition(context))
        {
            return ElectionVerificationPackageBlockerProto.VerificationPackageBlockerMissingPackage;
        }

        if (context.ProtocolPackageBinding is null ||
            context.ProtocolPackageBinding.Status != ProtocolPackageBindingStatus.Sealed)
        {
            return ElectionVerificationPackageBlockerProto.VerificationPackageBlockerProtocolRefs;
        }

        return ElectionVerificationPackageBlockerProto.VerificationPackageBlockerNone;
    }

    private static ElectionVerificationPackageBlockerProto MapExportBlocker(string code) =>
        code switch
        {
            VerificationResultCodes.ElectionNotFinalized =>
                ElectionVerificationPackageBlockerProto.VerificationPackageBlockerNotFinalized,
            VerificationResultCodes.RestrictedExportUnauthorized =>
                ElectionVerificationPackageBlockerProto.VerificationPackageBlockerUnauthorized,
            VerificationResultCodes.VerifierProfilePackageMismatch =>
                ElectionVerificationPackageBlockerProto.VerificationPackageBlockerProfileMismatch,
            VerificationResultCodes.PackageManifestMissingArtifact =>
                ElectionVerificationPackageBlockerProto.VerificationPackageBlockerMissingPackage,
            _ => ElectionVerificationPackageBlockerProto.VerificationPackageBlockerExportFailed,
        };

    private static string ResolveVerifierProfileId(VerificationPackageView packageView) =>
        packageView == VerificationPackageView.RestrictedOwnerAuditor
            ? VerificationProfileIds.RestrictedOwnerAuditorV1
            : VerificationProfileIds.PublicAnonymousV1;

    private static string ResolveBlockerCode(
        ElectionVerificationPackageBlockerProto blocker,
        VerificationPackageContext context) =>
        blocker switch
        {
            ElectionVerificationPackageBlockerProto.VerificationPackageBlockerNotVisible =>
                VerificationResultCodes.RestrictedExportUnauthorized,
            ElectionVerificationPackageBlockerProto.VerificationPackageBlockerNotFinalized =>
                VerificationResultCodes.ElectionNotFinalized,
            ElectionVerificationPackageBlockerProto.VerificationPackageBlockerMissingPackage =>
                VerificationResultCodes.PackageManifestMissingArtifact,
            ElectionVerificationPackageBlockerProto.VerificationPackageBlockerProtocolRefs =>
                VerificationResultCodes.VerifierProfilePackageMismatch,
            ElectionVerificationPackageBlockerProto.VerificationPackageBlockerUnauthorized =>
                VerificationResultCodes.RestrictedExportUnauthorized,
            ElectionVerificationPackageBlockerProto.VerificationPackageBlockerExportFailed =>
                context.LatestReportPackage?.FailureCode ?? VerificationResultCodes.PackageManifestMissingArtifact,
            _ => string.Empty,
        };

    private static string ResolveBlockerMessage(
        ElectionVerificationPackageBlockerProto blocker,
        VerificationPackageView packageView,
        VerificationPackageContext context) =>
        blocker switch
        {
            ElectionVerificationPackageBlockerProto.VerificationPackageBlockerNotVisible =>
                "Verification package controls are not visible to this actor.",
            ElectionVerificationPackageBlockerProto.VerificationPackageBlockerNotFinalized =>
                "The election must be finalized before a verification package can be exported.",
            ElectionVerificationPackageBlockerProto.VerificationPackageBlockerMissingPackage =>
                context.LatestReportPackage?.Status == ElectionReportPackageStatus.Sealed
                    ? "A sealed ballot definition is required before verification package export."
                    : "A sealed report package is required before verification package export.",
            ElectionVerificationPackageBlockerProto.VerificationPackageBlockerProtocolRefs =>
                "Sealed Protocol Omega package refs are required before verification package export.",
            ElectionVerificationPackageBlockerProto.VerificationPackageBlockerUnauthorized
                when packageView == VerificationPackageView.RestrictedOwnerAuditor =>
                "Restricted package export is limited to the owner/admin and designated auditor roles.",
            ElectionVerificationPackageBlockerProto.VerificationPackageBlockerExportFailed =>
                context.LatestReportPackage?.FailureReason ?? "Verification package export is currently blocked.",
            _ => string.Empty,
        };

    private static bool HasSealedBallotDefinition(VerificationPackageContext context) =>
        context.Election.BallotDefinitionVersion.HasValue &&
        context.Election.BallotDefinitionHash is { Length: > 0 } &&
        context.Election.BallotDefinitionSealedAt.HasValue;

    private static string ResolvePackageStatusMessage(
        ElectionVerificationPackageStatusProto status,
        VerificationPackageContext context) =>
        status switch
        {
            ElectionVerificationPackageStatusProto.VerificationPackageNotVisible =>
                "Verification package status is not visible to this actor.",
            ElectionVerificationPackageStatusProto.VerificationPackageNotFinalized =>
                "Verification package export becomes available after finalization.",
            ElectionVerificationPackageStatusProto.VerificationPackageMissing =>
                context.LatestReportPackage?.Status == ElectionReportPackageStatus.Sealed
                    ? "A sealed ballot definition is missing for this finalized election."
                    : "A sealed report package is missing for this finalized election.",
            ElectionVerificationPackageStatusProto.VerificationPackageProtocolRefsBlocked =>
                "Sealed Protocol Omega package refs are missing or incompatible.",
            ElectionVerificationPackageStatusProto.VerificationPackageExportFailed =>
                context.LatestReportPackage?.FailureReason ?? "The latest report package attempt failed.",
            ElectionVerificationPackageStatusProto.VerificationPackageReady =>
                context.ProtocolPackageBinding?.PackageApprovalStatus == ProtocolPackageApprovalStatus.DraftPrivate
                    ? "Verification package export is available with a DraftPrivate Protocol Omega package reference."
                    : "Verification package export is available.",
            _ => string.Empty,
        };

    private static bool CanRetryPackageExport(
        ElectionVerificationPackageBlockerProto blocker,
        VerificationPackageContext context) =>
        context.IsOwner &&
        (blocker == ElectionVerificationPackageBlockerProto.VerificationPackageBlockerMissingPackage ||
         blocker == ElectionVerificationPackageBlockerProto.VerificationPackageBlockerExportFailed);

    private static bool IsVerificationPackageVisible(bool isOwner, bool acceptedTrustee, bool isDesignatedAuditor) =>
        isOwner || acceptedTrustee || isDesignatedAuditor;

    private static ElectionVerifierResultSummaryView BuildVerifierResultNotAvailable() =>
        new()
        {
            OverallStatus = ElectionVerifierOverallStatusProto.ElectionVerifierNotAvailable,
            VerifierVersion = string.Empty,
            PackageHash = string.Empty,
            PassedCount = 0,
            WarningCount = 0,
            FailedCount = 0,
            NotApplicableCount = 0,
            Message = "No verifier output has been recorded for this package.",
            HasVerifiedAt = false,
        };

    private sealed record VerificationPackageContext(
        ElectionRecord Election,
        string ActorPublicAddress,
        bool IsOwner,
        bool AcceptedTrustee,
        bool IsDesignatedAuditor,
        ElectionReportPackageRecord? LatestReportPackage,
        ProtocolPackageBindingRecord? ProtocolPackageBinding,
        IReadOnlyList<ElectionTrusteeInvitationRecord> TrusteeInvitations,
        IReadOnlyList<ElectionReportArtifactRecord> ReportArtifacts,
        IReadOnlyList<ElectionBoundaryArtifactRecord> BoundaryArtifacts,
        IReadOnlyList<ElectionAcceptedBallotRecord> AcceptedBallots,
        IReadOnlyList<ElectionPublishedBallotRecord> PublishedBallots,
        IReadOnlyList<ElectionFinalizationSessionRecord> FinalizationSessions,
        IReadOnlyList<ElectionFinalizationShareRecord> FinalizationShares,
        IReadOnlyList<ElectionFinalizationReleaseEvidenceRecord> ReleaseEvidenceRecords,
        IReadOnlyList<ElectionCeremonyVersionRecord> CeremonyVersions,
        IReadOnlyList<ElectionCeremonyTrusteeStateRecord> CeremonyTrusteeStates,
        IReadOnlyList<ElectionCeremonyShareCustodyRecord> CeremonyShareCustodyRecords,
        IReadOnlyList<ElectionRosterEntryRecord> RosterEntries,
        IReadOnlyList<ElectionParticipationRecord> ParticipationRecords,
        IReadOnlyList<ElectionVoterCeremonyRecord> VoterCeremonyRecords,
        IReadOnlyList<ElectionPreparedBallotCommitmentRecord> PreparedBallotCommitments,
        IReadOnlyList<ElectionSpoiledPreparedBallotRecord> SpoiledPreparedBallots,
        IReadOnlyList<ElectionRosterImportEvidenceRecord> RosterImportEvidences,
        IReadOnlyList<ElectionEligibilityPolicyEvidenceRecord> EligibilityPolicyEvidences,
        IReadOnlyList<ElectionCommitmentSchemeEvidenceRecord> CommitmentSchemeEvidences,
        IReadOnlyList<ElectionCommitmentRegistrationRecord> CommitmentRegistrations,
        IReadOnlyList<ElectionCheckoffConsumptionRecord> CheckoffConsumptions,
        IReadOnlyList<ElectionEligibilityActivationEventRecord> EligibilityActivationEvents,
        IReadOnlyList<ElectionPublicationWitnessRecord> PublicationWitnesses,
        IReadOnlyList<ElectionPublicationProofSessionRecord> PublicationProofSessions,
        IReadOnlyList<ElectionPublicationProofTranscriptRecord> PublicationProofTranscripts,
        IReadOnlyList<ElectionPublicationWitnessDeletionReceiptRecord> PublicationWitnessDeletionReceipts)
    {
        public bool CanViewPackageStatus => IsVerificationPackageVisible(IsOwner, AcceptedTrustee, IsDesignatedAuditor);

        public bool CanExportRestrictedPackage => IsOwner || IsDesignatedAuditor;
    }
}
