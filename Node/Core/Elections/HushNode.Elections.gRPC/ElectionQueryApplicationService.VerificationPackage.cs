using HushNetwork.proto;
using HushNode.Elections;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;

namespace HushNode.Elections.gRPC;

public partial class ElectionQueryApplicationService
{
    private const string VerifierResultNotAvailableCode = "verifier_result_not_available";

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
            EligibilityActivationEvents: []);

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
            eligibilityActivationEvents);
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
            TrusteeControlDomainRecords: BuildSp06ControlDomainRecords(context));

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
        IReadOnlyList<ElectionEligibilityActivationEventRecord> EligibilityActivationEvents)
    {
        public bool CanViewPackageStatus => IsVerificationPackageVisible(IsOwner, AcceptedTrustee, IsDesignatedAuditor);

        public bool CanExportRestrictedPackage => IsOwner || IsDesignatedAuditor;
    }
}
