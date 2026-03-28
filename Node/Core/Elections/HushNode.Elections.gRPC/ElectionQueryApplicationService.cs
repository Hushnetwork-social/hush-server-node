using HushNetwork.proto;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections.gRPC;

public class ElectionQueryApplicationService : IElectionQueryApplicationService
{
    private readonly IUnitOfWorkProvider<ElectionsDbContext> _unitOfWorkProvider;
    private readonly ElectionCeremonyOptions _ceremonyOptions;

    public ElectionQueryApplicationService(IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider)
        : this(unitOfWorkProvider, new ElectionCeremonyOptions())
    {
    }

    public ElectionQueryApplicationService(
        IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider,
        ElectionCeremonyOptions ceremonyOptions)
    {
        _unitOfWorkProvider = unitOfWorkProvider;
        _ceremonyOptions = ceremonyOptions;
    }

    public async Task<GetElectionResponse> GetElectionAsync(ElectionId electionId)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();

        var election = await repository.GetElectionAsync(electionId);
        if (election is null)
        {
            return new GetElectionResponse
            {
                Success = false,
                ErrorMessage = $"Election {electionId} was not found.",
            };
        }

        var latestDraftSnapshot = await repository.GetLatestDraftSnapshotAsync(electionId);
        var warningAcknowledgements = await repository.GetWarningAcknowledgementsAsync(electionId);
        var trusteeInvitations = await repository.GetTrusteeInvitationsAsync(electionId);
        var boundaryArtifacts = await repository.GetBoundaryArtifactsAsync(electionId);
        var governedProposals = await repository.GetGovernedProposalsAsync(electionId);
        var governedProposalApprovals = new List<ElectionGovernedProposalApprovalRecord>();
        foreach (var proposal in governedProposals)
        {
            governedProposalApprovals.AddRange(await repository.GetGovernedProposalApprovalsAsync(proposal.Id));
        }

        var ceremonyProfiles = await repository.GetCeremonyProfilesAsync();
        var visibleCeremonyProfiles = FilterVisibleCeremonyProfiles(ceremonyProfiles);
        var ceremonyVersions = await repository.GetCeremonyVersionsAsync(electionId);
        var ceremonyTranscriptEvents = new List<ElectionCeremonyTranscriptEventRecord>();
        foreach (var version in ceremonyVersions.OrderByDescending(x => x.VersionNumber))
        {
            ceremonyTranscriptEvents.AddRange(await repository.GetCeremonyTranscriptEventsAsync(version.Id));
        }

        var activeCeremonyVersion = await repository.GetActiveCeremonyVersionAsync(electionId);
        var activeCeremonyTrusteeStates = activeCeremonyVersion is null
            ? Array.Empty<ElectionCeremonyTrusteeStateRecord>()
            : await repository.GetCeremonyTrusteeStatesAsync(activeCeremonyVersion.Id);
        var finalizationSessions = await repository.GetFinalizationSessionsAsync(electionId);
        var finalizationShares = new List<ElectionFinalizationShareRecord>();
        foreach (var finalizationSession in finalizationSessions)
        {
            finalizationShares.AddRange(await repository.GetFinalizationSharesAsync(finalizationSession.Id));
        }

        var finalizationReleaseEvidenceRecords = await repository.GetFinalizationReleaseEvidenceRecordsAsync(electionId);

        var response = new GetElectionResponse
        {
            Success = true,
            Election = election.ToProto(),
            ErrorMessage = string.Empty,
        };

        if (latestDraftSnapshot is not null)
        {
            response.LatestDraftSnapshot = latestDraftSnapshot.ToProto();
        }

        response.WarningAcknowledgements.AddRange(warningAcknowledgements.Select(x => x.ToProto()));
        response.TrusteeInvitations.AddRange(trusteeInvitations.Select(x => x.ToProto()));
        response.BoundaryArtifacts.AddRange(boundaryArtifacts.Select(x => x.ToProto()));
        response.GovernedProposals.AddRange(governedProposals.Select(x => x.ToProto()));
        response.GovernedProposalApprovals.AddRange(governedProposalApprovals
            .OrderBy(x => x.ApprovedAt)
            .Select(x => x.ToProto()));
        response.CeremonyProfiles.AddRange(visibleCeremonyProfiles.Select(x => x.ToProto()));
        response.CeremonyVersions.AddRange(ceremonyVersions
            .OrderByDescending(x => x.VersionNumber)
            .Select(x => x.ToProto()));
        response.CeremonyTranscriptEvents.AddRange(ceremonyTranscriptEvents
            .OrderByDescending(x => x.VersionNumber)
            .ThenBy(x => x.OccurredAt)
            .Select(x => x.ToProto()));
        response.ActiveCeremonyTrusteeStates.AddRange(activeCeremonyTrusteeStates
            .OrderBy(x => x.TrusteeDisplayName ?? x.TrusteeUserAddress)
            .Select(x => x.ToProto()));
        response.FinalizationSessions.AddRange(finalizationSessions.Select(x => x.ToProto()));
        response.FinalizationShares.AddRange(finalizationShares.Select(x => x.ToProto()));
        response.FinalizationReleaseEvidenceRecords.AddRange(finalizationReleaseEvidenceRecords.Select(x => x.ToProto()));

        return response;
    }

    public async Task<GetElectionEnvelopeAccessResponse> GetElectionEnvelopeAccessAsync(
        ElectionId electionId,
        string actorPublicAddress)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();

        var accessRecord = await repository.GetElectionEnvelopeAccessAsync(electionId, actorPublicAddress);
        if (accessRecord is null)
        {
            return new GetElectionEnvelopeAccessResponse
            {
                Success = false,
                ErrorMessage = $"No election envelope access was found for actor '{actorPublicAddress}' on election {electionId}.",
            };
        }

        return new GetElectionEnvelopeAccessResponse
        {
            Success = true,
            ErrorMessage = string.Empty,
            ActorEncryptedElectionPrivateKey = accessRecord.ActorEncryptedElectionPrivateKey,
        };
    }

    public async Task<GetElectionCeremonyActionViewResponse> GetElectionCeremonyActionViewAsync(
        ElectionId electionId,
        string actorPublicAddress)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();

        var election = await repository.GetElectionAsync(electionId);
        if (election is null)
        {
            return new GetElectionCeremonyActionViewResponse
            {
                Success = false,
                ErrorMessage = $"Election {electionId} was not found.",
                ActorPublicAddress = actorPublicAddress,
            };
        }

        var trusteeInvitations = await repository.GetTrusteeInvitationsAsync(electionId);
        var visibleProfiles = FilterVisibleCeremonyProfiles(await repository.GetCeremonyProfilesAsync());
        var activeCeremonyVersion = await repository.GetActiveCeremonyVersionAsync(electionId);
        var activeCeremonyTrusteeStates = activeCeremonyVersion is null
            ? Array.Empty<ElectionCeremonyTrusteeStateRecord>()
            : await repository.GetCeremonyTrusteeStatesAsync(activeCeremonyVersion.Id);
        var selfTrusteeState = activeCeremonyTrusteeStates.FirstOrDefault(x =>
            string.Equals(x.TrusteeUserAddress, actorPublicAddress, StringComparison.OrdinalIgnoreCase));
        var selfShareCustody = activeCeremonyVersion is null
            ? null
            : await repository.GetCeremonyShareCustodyRecordAsync(activeCeremonyVersion.Id, actorPublicAddress);
        var pendingIncomingMessageCount = activeCeremonyVersion is null
            ? 0
            : (await repository.GetCeremonyMessageEnvelopesForRecipientAsync(activeCeremonyVersion.Id, actorPublicAddress)).Count;

        var actorRole = ResolveActorRole(election, trusteeInvitations, actorPublicAddress);
        var response = new GetElectionCeremonyActionViewResponse
        {
            Success = true,
            ErrorMessage = string.Empty,
            ActorRole = actorRole,
            ActorPublicAddress = actorPublicAddress,
            PendingIncomingMessageCount = pendingIncomingMessageCount,
        };

        if (activeCeremonyVersion is not null)
        {
            response.ActiveCeremonyVersion = activeCeremonyVersion.ToProto();
        }

        if (selfTrusteeState is not null)
        {
            response.SelfTrusteeState = selfTrusteeState.ToProto();
        }

        if (selfShareCustody is not null)
        {
            response.SelfShareCustody = selfShareCustody.ToProto();
        }

        if (actorRole == ElectionCeremonyActorRoleProto.CeremonyActorOwner)
        {
            var ownerActions = BuildOwnerActions(
                election,
                trusteeInvitations,
                visibleProfiles,
                activeCeremonyVersion);
            response.OwnerActions.AddRange(ownerActions);
            response.BlockedReasons.AddRange(ownerActions
                .Where(x => !x.IsAvailable && !x.IsCompleted && !string.IsNullOrWhiteSpace(x.Reason))
                .Select(x => x.Reason)
                .Distinct(StringComparer.Ordinal));
        }

        if (actorRole == ElectionCeremonyActorRoleProto.CeremonyActorTrustee)
        {
            var trusteeActions = BuildTrusteeActions(
                activeCeremonyVersion,
                selfTrusteeState,
                selfShareCustody);
            response.TrusteeActions.AddRange(trusteeActions);
            response.BlockedReasons.AddRange(trusteeActions
                .Where(x => !x.IsAvailable && !x.IsCompleted && !string.IsNullOrWhiteSpace(x.Reason))
                .Select(x => x.Reason)
                .Distinct(StringComparer.Ordinal));
        }

        if (actorRole == ElectionCeremonyActorRoleProto.CeremonyActorReadOnly)
        {
            response.BlockedReasons.Add("No ceremony actions are available for this actor.");
        }

        return response;
    }

    public async Task<GetElectionsByOwnerResponse> GetElectionsByOwnerAsync(string ownerPublicAddress)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var elections = await repository.GetElectionsByOwnerAsync(ownerPublicAddress);

        var response = new GetElectionsByOwnerResponse();
        response.Elections.AddRange(elections.Select(x => x.ToSummaryProto()));
        return response;
    }

    private IReadOnlyList<ElectionCeremonyProfileRecord> FilterVisibleCeremonyProfiles(
        IReadOnlyList<ElectionCeremonyProfileRecord> ceremonyProfiles) =>
        ceremonyProfiles
            .Where(x => _ceremonyOptions.EnableDevCeremonyProfiles || !x.DevOnly)
            .OrderBy(x => x.DevOnly)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static ElectionCeremonyActorRoleProto ResolveActorRole(
        ElectionRecord election,
        IReadOnlyList<ElectionTrusteeInvitationRecord> trusteeInvitations,
        string actorPublicAddress)
    {
        if (string.Equals(election.OwnerPublicAddress, actorPublicAddress, StringComparison.OrdinalIgnoreCase))
        {
            return ElectionCeremonyActorRoleProto.CeremonyActorOwner;
        }

        var hasAcceptedTrusteeInvitation = trusteeInvitations.Any(x =>
            x.Status == ElectionTrusteeInvitationStatus.Accepted &&
            string.Equals(x.TrusteeUserAddress, actorPublicAddress, StringComparison.OrdinalIgnoreCase));

        return hasAcceptedTrusteeInvitation
            ? ElectionCeremonyActorRoleProto.CeremonyActorTrustee
            : ElectionCeremonyActorRoleProto.CeremonyActorReadOnly;
    }

    private static IReadOnlyList<ElectionCeremonyActionAvailability> BuildOwnerActions(
        ElectionRecord election,
        IReadOnlyList<ElectionTrusteeInvitationRecord> trusteeInvitations,
        IReadOnlyList<ElectionCeremonyProfileRecord> visibleProfiles,
        ElectionCeremonyVersionRecord? activeCeremonyVersion)
    {
        var acceptedTrustees = trusteeInvitations
            .Where(x => x.Status == ElectionTrusteeInvitationStatus.Accepted)
            .ToArray();
        var hasMatchingProfile = election.RequiredApprovalCount.HasValue &&
            visibleProfiles.Any(x =>
                x.TrusteeCount == acceptedTrustees.Length &&
                x.RequiredApprovalCount == election.RequiredApprovalCount.Value);

        return
        [
            BuildOwnerAction(
                ElectionCeremonyActionTypeProto.CeremonyActionStartVersion,
                election,
                activeCeremonyVersion,
                acceptedTrustees.Length,
                hasMatchingProfile),
            BuildOwnerAction(
                ElectionCeremonyActionTypeProto.CeremonyActionRestartVersion,
                election,
                activeCeremonyVersion,
                acceptedTrustees.Length,
                hasMatchingProfile),
        ];
    }

    private static ElectionCeremonyActionAvailability BuildOwnerAction(
        ElectionCeremonyActionTypeProto actionType,
        ElectionRecord election,
        ElectionCeremonyVersionRecord? activeCeremonyVersion,
        int acceptedTrusteeCount,
        bool hasMatchingProfile)
    {
        var isStart = actionType == ElectionCeremonyActionTypeProto.CeremonyActionStartVersion;
        var isDraft = election.LifecycleState == ElectionLifecycleState.Draft;

        if (!isDraft)
        {
            return CreateAction(actionType, false, false, "Ceremony versions can only be changed while the election is in draft.");
        }

        if (!election.RequiredApprovalCount.HasValue)
        {
            return CreateAction(actionType, false, false, "This election does not require a trustee-threshold ceremony.");
        }

        if (acceptedTrusteeCount == 0)
        {
            return CreateAction(actionType, false, false, "At least one accepted trustee is required before a ceremony can begin.");
        }

        if (!hasMatchingProfile)
        {
            return CreateAction(actionType, false, false, "No allowed ceremony profile matches the current accepted trustee roster and threshold.");
        }

        if (isStart)
        {
            return activeCeremonyVersion is null
                ? CreateAction(actionType, true, false, "Start a ceremony version for the current accepted trustee roster.")
                : CreateAction(actionType, false, false, "An active ceremony version already exists. Restart it to supersede the current version.");
        }

        return activeCeremonyVersion is null
            ? CreateAction(actionType, false, false, "No active ceremony version exists yet.")
            : CreateAction(actionType, true, false, "Restarting supersedes the current ceremony version and starts a fresh one.");
    }

    private static IReadOnlyList<ElectionCeremonyActionAvailability> BuildTrusteeActions(
        ElectionCeremonyVersionRecord? activeCeremonyVersion,
        ElectionCeremonyTrusteeStateRecord? selfTrusteeState,
        ElectionCeremonyShareCustodyRecord? selfShareCustody)
    {
        if (activeCeremonyVersion is null)
        {
            return BuildUnavailableTrusteeActions("No active ceremony version is available yet.");
        }

        if (selfTrusteeState is null)
        {
            return BuildUnavailableTrusteeActions("You are not bound to the active ceremony version.");
        }

        if (selfTrusteeState.State == ElectionTrusteeCeremonyState.Removed)
        {
            return BuildUnavailableTrusteeActions("You were removed from this ceremony version.");
        }

        var publishCompleted = selfTrusteeState.HasPublishedTransportKey;
        var joinCompleted = selfTrusteeState.JoinedAt.HasValue || selfTrusteeState.State >= ElectionTrusteeCeremonyState.CeremonyJoined;
        var selfTestCompleted = selfTrusteeState.SelfTestSucceededAt.HasValue &&
            selfTrusteeState.State != ElectionTrusteeCeremonyState.CeremonyValidationFailed;
        var submitCompleted = selfTrusteeState.State == ElectionTrusteeCeremonyState.CeremonyMaterialSubmitted ||
            selfTrusteeState.State == ElectionTrusteeCeremonyState.CeremonyCompleted;
        var exportCompleted = selfShareCustody is not null &&
            selfShareCustody.Status != ElectionCeremonyShareCustodyStatus.NotExported;
        var importCompleted = selfShareCustody?.Status == ElectionCeremonyShareCustodyStatus.Imported;

        return
        [
            CreateAction(
                ElectionCeremonyActionTypeProto.CeremonyActionPublishTransportKey,
                !publishCompleted,
                publishCompleted,
                publishCompleted
                    ? "Transport key already published for this version."
                    : "Publish your ceremony transport key first."),
            CreateAction(
                ElectionCeremonyActionTypeProto.CeremonyActionJoinVersion,
                !joinCompleted && publishCompleted &&
                (selfTrusteeState.State == ElectionTrusteeCeremonyState.AcceptedTrustee ||
                 selfTrusteeState.State == ElectionTrusteeCeremonyState.CeremonyNotStarted),
                joinCompleted,
                joinCompleted
                    ? "You already joined the active ceremony version."
                    : !publishCompleted
                        ? "Publish the transport key before joining the ceremony."
                        : "Join the active ceremony version."),
            CreateAction(
                ElectionCeremonyActionTypeProto.CeremonyActionRunSelfTest,
                !selfTestCompleted &&
                (selfTrusteeState.State == ElectionTrusteeCeremonyState.CeremonyJoined ||
                 selfTrusteeState.State == ElectionTrusteeCeremonyState.CeremonyValidationFailed),
                selfTestCompleted,
                selfTestCompleted
                    ? "Mandatory self-test already completed for this submission cycle."
                    : selfTrusteeState.State == ElectionTrusteeCeremonyState.CeremonyValidationFailed
                        ? "Validation failed previously. Run the self-test again before resubmitting."
                        : joinCompleted
                            ? "Run the mandatory self-test before submitting ceremony material."
                            : "Join the ceremony version before running the self-test."),
            CreateAction(
                ElectionCeremonyActionTypeProto.CeremonyActionSubmitMaterial,
                !submitCompleted &&
                selfTrusteeState.SelfTestSucceededAt.HasValue &&
                (selfTrusteeState.State == ElectionTrusteeCeremonyState.CeremonyJoined ||
                 selfTrusteeState.State == ElectionTrusteeCeremonyState.CeremonyValidationFailed),
                submitCompleted,
                submitCompleted
                    ? "Ceremony material already submitted for this version."
                    : !selfTrusteeState.SelfTestSucceededAt.HasValue
                        ? "Run the mandatory self-test before submitting ceremony material."
                        : "Submit ceremony material for validation."),
            CreateAction(
                ElectionCeremonyActionTypeProto.CeremonyActionExportShare,
                selfShareCustody is not null &&
                selfTrusteeState.State == ElectionTrusteeCeremonyState.CeremonyCompleted &&
                selfShareCustody.Status == ElectionCeremonyShareCustodyStatus.NotExported,
                exportCompleted,
                exportCompleted
                    ? "Encrypted share backup already exported."
                    : selfTrusteeState.State == ElectionTrusteeCeremonyState.CeremonyCompleted
                        ? "Export the encrypted share backup and store it safely."
                        : "Share export becomes available after ceremony completion."),
            CreateAction(
                ElectionCeremonyActionTypeProto.CeremonyActionImportShare,
                selfShareCustody is not null &&
                selfTrusteeState.State == ElectionTrusteeCeremonyState.CeremonyCompleted,
                importCompleted,
                importCompleted
                    ? "A share import was already recorded for this version."
                    : selfTrusteeState.State == ElectionTrusteeCeremonyState.CeremonyCompleted
                        ? "Import an exact-bound backup when moving to another device."
                        : "Share import becomes available after ceremony completion."),
        ];
    }

    private static IReadOnlyList<ElectionCeremonyActionAvailability> BuildUnavailableTrusteeActions(string reason) =>
    [
        CreateAction(ElectionCeremonyActionTypeProto.CeremonyActionPublishTransportKey, false, false, reason),
        CreateAction(ElectionCeremonyActionTypeProto.CeremonyActionJoinVersion, false, false, reason),
        CreateAction(ElectionCeremonyActionTypeProto.CeremonyActionRunSelfTest, false, false, reason),
        CreateAction(ElectionCeremonyActionTypeProto.CeremonyActionSubmitMaterial, false, false, reason),
        CreateAction(ElectionCeremonyActionTypeProto.CeremonyActionExportShare, false, false, reason),
        CreateAction(ElectionCeremonyActionTypeProto.CeremonyActionImportShare, false, false, reason),
    ];

    private static ElectionCeremonyActionAvailability CreateAction(
        ElectionCeremonyActionTypeProto actionType,
        bool isAvailable,
        bool isCompleted,
        string reason) =>
        new()
        {
            ActionType = actionType,
            IsAvailable = isAvailable,
            IsCompleted = isCompleted,
            Reason = reason,
        };
}
