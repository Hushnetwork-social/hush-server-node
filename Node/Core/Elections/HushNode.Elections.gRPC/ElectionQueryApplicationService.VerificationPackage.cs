using HushNetwork.proto;
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
            ReportArtifacts: [],
            BoundaryArtifacts: [],
            AcceptedBallots: [],
            PublishedBallots: [],
            FinalizationSessions: [],
            FinalizationShares: [],
            ReleaseEvidenceRecords: [],
            RosterEntries: [],
            ParticipationRecords: []);

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
        var rosterEntries = await repository.GetRosterEntriesAsync(election.ElectionId);
        var participationRecords = await repository.GetParticipationRecordsAsync(election.ElectionId);

        return new VerificationPackageContext(
            election,
            actorPublicAddress,
            isOwner,
            acceptedTrustee,
            isDesignatedAuditor,
            latestReportPackage,
            protocolPackageBinding,
            reportArtifacts,
            boundaryArtifacts,
            acceptedBallots,
            publishedBallots,
            finalizationSessions,
            finalizationShares,
            releaseEvidence,
            rosterEntries,
            participationRecords);
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
        };

        if (context.CanViewPackageStatus && context.ProtocolPackageBinding is not null)
        {
            view.ProtocolPackageBinding = context.ProtocolPackageBinding.ToProto();
        }

        return view;
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
            context.Election.FinalizedAt);

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
                "A sealed report package is required before verification package export.",
            ElectionVerificationPackageBlockerProto.VerificationPackageBlockerProtocolRefs =>
                "Sealed Protocol Omega package refs are required before verification package export.",
            ElectionVerificationPackageBlockerProto.VerificationPackageBlockerUnauthorized
                when packageView == VerificationPackageView.RestrictedOwnerAuditor =>
                "Restricted package export is limited to the owner/admin and designated auditor roles.",
            ElectionVerificationPackageBlockerProto.VerificationPackageBlockerExportFailed =>
                context.LatestReportPackage?.FailureReason ?? "Verification package export is currently blocked.",
            _ => string.Empty,
        };

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
                "A sealed report package is missing for this finalized election.",
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
        IReadOnlyList<ElectionReportArtifactRecord> ReportArtifacts,
        IReadOnlyList<ElectionBoundaryArtifactRecord> BoundaryArtifacts,
        IReadOnlyList<ElectionAcceptedBallotRecord> AcceptedBallots,
        IReadOnlyList<ElectionPublishedBallotRecord> PublishedBallots,
        IReadOnlyList<ElectionFinalizationSessionRecord> FinalizationSessions,
        IReadOnlyList<ElectionFinalizationShareRecord> FinalizationShares,
        IReadOnlyList<ElectionFinalizationReleaseEvidenceRecord> ReleaseEvidenceRecords,
        IReadOnlyList<ElectionRosterEntryRecord> RosterEntries,
        IReadOnlyList<ElectionParticipationRecord> ParticipationRecords)
    {
        public bool CanViewPackageStatus => IsVerificationPackageVisible(IsOwner, AcceptedTrustee, IsDesignatedAuditor);

        public bool CanExportRestrictedPackage => IsOwner || IsDesignatedAuditor;
    }
}
