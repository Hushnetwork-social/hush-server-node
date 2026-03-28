using HushNetwork.proto;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Olimpo.EntityFramework.Persistency;
using Timestamp = Google.Protobuf.WellKnownTypes.Timestamp;

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

    public async Task<GetElectionEligibilityViewResponse> GetElectionEligibilityViewAsync(
        ElectionId electionId,
        string actorPublicAddress)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var normalizedActorPublicAddress = actorPublicAddress?.Trim() ?? string.Empty;

        var election = await repository.GetElectionAsync(electionId);
        if (election is null)
        {
            return new GetElectionEligibilityViewResponse
            {
                Success = false,
                ErrorMessage = $"Election {electionId} was not found.",
                ActorPublicAddress = normalizedActorPublicAddress,
            };
        }

        var trusteeInvitations = await repository.GetTrusteeInvitationsAsync(electionId);
        var rosterEntries = await repository.GetRosterEntriesAsync(electionId);
        var participationRecords = await repository.GetParticipationRecordsAsync(electionId);
        var activationEvents = await repository.GetEligibilityActivationEventsAsync(electionId);
        var snapshots = await repository.GetEligibilitySnapshotsAsync(electionId);
        var selfRosterEntry = string.IsNullOrWhiteSpace(normalizedActorPublicAddress)
            ? null
            : await repository.GetRosterEntryByLinkedActorAsync(electionId, normalizedActorPublicAddress);
        var actorRole = ResolveEligibilityActorRole(
            election,
            trusteeInvitations,
            selfRosterEntry,
            normalizedActorPublicAddress);
        var participationLookup = participationRecords.ToDictionary(
            x => x.OrganizationVoterId,
            StringComparer.OrdinalIgnoreCase);
        var canReviewRestrictedRoster =
            actorRole == ElectionEligibilityActorRoleProto.EligibilityActorOwner ||
            actorRole == ElectionEligibilityActorRoleProto.EligibilityActorRestrictedReviewer;

        var response = new GetElectionEligibilityViewResponse
        {
            Success = true,
            ErrorMessage = string.Empty,
            ActorPublicAddress = normalizedActorPublicAddress,
            ActorRole = actorRole,
            CanImportRoster =
                actorRole == ElectionEligibilityActorRoleProto.EligibilityActorOwner &&
                election.LifecycleState == ElectionLifecycleState.Draft,
            CanActivateRoster =
                actorRole == ElectionEligibilityActorRoleProto.EligibilityActorOwner &&
                election.LifecycleState == ElectionLifecycleState.Open &&
                election.EligibilityMutationPolicy == EligibilityMutationPolicy.LateActivationForRosteredVotersOnly,
            CanReviewRestrictedRoster = canReviewRestrictedRoster,
            CanClaimIdentity =
                !string.IsNullOrWhiteSpace(normalizedActorPublicAddress) &&
                actorRole != ElectionEligibilityActorRoleProto.EligibilityActorOwner &&
                selfRosterEntry is null &&
                (election.LifecycleState == ElectionLifecycleState.Draft ||
                 election.LifecycleState == ElectionLifecycleState.Open),
            UsesTemporaryVerificationCode = true,
            TemporaryVerificationCode = ElectionEligibilityContracts.TemporaryVerificationCode,
            Summary = BuildEligibilitySummary(
                election,
                rosterEntries,
                participationLookup,
                activationEvents.Count),
        };

        if (selfRosterEntry is not null)
        {
            response.SelfRosterEntry = ToEligibilityRosterEntryView(
                election,
                selfRosterEntry,
                participationLookup.GetValueOrDefault(selfRosterEntry.OrganizationVoterId));
        }

        if (canReviewRestrictedRoster)
        {
            response.RestrictedRosterEntries.AddRange(rosterEntries
                .OrderBy(x => x.OrganizationVoterId, StringComparer.OrdinalIgnoreCase)
                .Select(x => ToEligibilityRosterEntryView(
                    election,
                    x,
                    participationLookup.GetValueOrDefault(x.OrganizationVoterId))));
            response.ActivationEvents.AddRange(activationEvents
                .OrderByDescending(x => x.OccurredAt)
                .ThenByDescending(x => x.Id)
                .Select(ToEligibilityActivationEventView));
            response.EligibilitySnapshots.AddRange(snapshots
                .OrderBy(x => x.RecordedAt)
                .ThenBy(x => x.SnapshotType)
                .Select(ToEligibilitySnapshotView));
        }

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

    private static ElectionEligibilitySummaryView BuildEligibilitySummary(
        ElectionRecord election,
        IReadOnlyList<ElectionRosterEntryRecord> rosterEntries,
        IReadOnlyDictionary<string, ElectionParticipationRecord> participationLookup,
        int activationEventCount)
    {
        var scopedRosterEntries = ResolveScopedRosterEntries(election, rosterEntries);
        var denominatorEntries = ResolveCurrentDenominatorEntries(election, rosterEntries);
        var countedParticipationCount = denominatorEntries.Count(x =>
            participationLookup.TryGetValue(x.OrganizationVoterId, out var participation) &&
            participation.CountsAsParticipation);
        var blankCount = denominatorEntries.Count(x =>
            participationLookup.TryGetValue(x.OrganizationVoterId, out var participation) &&
            participation.ParticipationStatus == ElectionParticipationStatus.Blank);

        return new ElectionEligibilitySummaryView
        {
            RosteredCount = scopedRosterEntries.Count,
            LinkedCount = scopedRosterEntries.Count(x => x.IsLinked),
            ActiveCount = scopedRosterEntries.Count(x => x.IsActive),
            ActiveAtOpenCount = scopedRosterEntries.Count(x => x.WasActiveAtOpen),
            CurrentDenominatorCount = denominatorEntries.Count,
            CountedParticipationCount = countedParticipationCount,
            BlankCount = blankCount,
            DidNotVoteCount = Math.Max(0, denominatorEntries.Count - countedParticipationCount),
            ActivationEventCount = activationEventCount,
        };
    }

    private IReadOnlyList<ElectionCeremonyProfileRecord> FilterVisibleCeremonyProfiles(
        IReadOnlyList<ElectionCeremonyProfileRecord> ceremonyProfiles) =>
        ceremonyProfiles
            .Where(x => _ceremonyOptions.EnableDevCeremonyProfiles || !x.DevOnly)
            .OrderBy(x => x.DevOnly)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static ElectionEligibilityActorRoleProto ResolveEligibilityActorRole(
        ElectionRecord election,
        IReadOnlyList<ElectionTrusteeInvitationRecord> trusteeInvitations,
        ElectionRosterEntryRecord? selfRosterEntry,
        string actorPublicAddress)
    {
        if (!string.IsNullOrWhiteSpace(actorPublicAddress) &&
            string.Equals(election.OwnerPublicAddress, actorPublicAddress, StringComparison.OrdinalIgnoreCase))
        {
            return ElectionEligibilityActorRoleProto.EligibilityActorOwner;
        }

        var hasAcceptedTrusteeInvitation = !string.IsNullOrWhiteSpace(actorPublicAddress) &&
            trusteeInvitations.Any(x =>
                x.Status == ElectionTrusteeInvitationStatus.Accepted &&
                string.Equals(x.TrusteeUserAddress, actorPublicAddress, StringComparison.OrdinalIgnoreCase));
        if (hasAcceptedTrusteeInvitation)
        {
            return ElectionEligibilityActorRoleProto.EligibilityActorRestrictedReviewer;
        }

        return selfRosterEntry is not null
            ? ElectionEligibilityActorRoleProto.EligibilityActorLinkedVoter
            : ElectionEligibilityActorRoleProto.EligibilityActorReadOnly;
    }

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

    private static ElectionRosterEntryView ToEligibilityRosterEntryView(
        ElectionRecord election,
        ElectionRosterEntryRecord rosterEntry,
        ElectionParticipationRecord? participationRecord) =>
        new()
        {
            ElectionId = rosterEntry.ElectionId.ToString(),
            OrganizationVoterId = rosterEntry.OrganizationVoterId,
            ContactType = (ElectionRosterContactTypeProto)(int)rosterEntry.ContactType,
            ContactValueHint = BuildContactValueHint(rosterEntry.ContactType, rosterEntry.ContactValue),
            LinkStatus = (ElectionVoterLinkStatusProto)(int)rosterEntry.LinkStatus,
            VotingRightStatus = (ElectionVotingRightStatusProto)(int)rosterEntry.VotingRightStatus,
            WasPresentAtOpen = rosterEntry.WasPresentAtOpen,
            WasActiveAtOpen = rosterEntry.WasActiveAtOpen,
            InCurrentDenominator = IsInCurrentDenominator(election, rosterEntry),
            ParticipationStatus = (ElectionParticipationStatusProto)(int)(participationRecord?.ParticipationStatus
                ?? ElectionParticipationStatus.DidNotVote),
            CountsAsParticipation = participationRecord?.CountsAsParticipation ?? false,
        };

    private static ElectionEligibilityActivationEventView ToEligibilityActivationEventView(
        ElectionEligibilityActivationEventRecord activationEvent) =>
        new()
        {
            Id = activationEvent.Id.ToString(),
            ElectionId = activationEvent.ElectionId.ToString(),
            OrganizationVoterId = activationEvent.OrganizationVoterId,
            AttemptedByPublicAddress = activationEvent.AttemptedByPublicAddress,
            Outcome = (ElectionEligibilityActivationOutcomeProto)(int)activationEvent.Outcome,
            BlockReason = (ElectionEligibilityActivationBlockReasonProto)(int)activationEvent.BlockReason,
            OccurredAt = ToProtoTimestamp(activationEvent.OccurredAt),
        };

    private static ElectionEligibilitySnapshotView ToEligibilitySnapshotView(
        ElectionEligibilitySnapshotRecord snapshot) =>
        new()
        {
            Id = snapshot.Id.ToString(),
            ElectionId = snapshot.ElectionId.ToString(),
            SnapshotType = (ElectionEligibilitySnapshotTypeProto)(int)snapshot.SnapshotType,
            EligibilityMutationPolicy = (EligibilityMutationPolicyProto)(int)snapshot.EligibilityMutationPolicy,
            RosteredCount = snapshot.RosteredCount,
            LinkedCount = snapshot.LinkedCount,
            ActiveDenominatorCount = snapshot.ActiveDenominatorCount,
            CountedParticipationCount = snapshot.CountedParticipationCount,
            BlankCount = snapshot.BlankCount,
            DidNotVoteCount = snapshot.DidNotVoteCount,
            RosteredVoterSetHash = EncodeHash(snapshot.RosteredVoterSetHash),
            ActiveDenominatorSetHash = EncodeHash(snapshot.ActiveDenominatorSetHash),
            CountedParticipationSetHash = EncodeHash(snapshot.CountedParticipationSetHash),
            BoundaryArtifactId = snapshot.BoundaryArtifactId?.ToString() ?? string.Empty,
            RecordedAt = ToProtoTimestamp(snapshot.RecordedAt),
            RecordedByPublicAddress = snapshot.RecordedByPublicAddress,
        };

    private static IReadOnlyList<ElectionRosterEntryRecord> ResolveScopedRosterEntries(
        ElectionRecord election,
        IReadOnlyList<ElectionRosterEntryRecord> rosterEntries) =>
        election.LifecycleState == ElectionLifecycleState.Draft
            ? rosterEntries
            : rosterEntries.Where(x => x.WasPresentAtOpen).ToArray();

    private static IReadOnlyList<ElectionRosterEntryRecord> ResolveCurrentDenominatorEntries(
        ElectionRecord election,
        IReadOnlyList<ElectionRosterEntryRecord> rosterEntries)
    {
        var scopedRosterEntries = ResolveScopedRosterEntries(election, rosterEntries);
        if (election.LifecycleState == ElectionLifecycleState.Draft)
        {
            return scopedRosterEntries.Where(x => x.IsActive).ToArray();
        }

        return election.EligibilityMutationPolicy switch
        {
            EligibilityMutationPolicy.FrozenAtOpen => scopedRosterEntries
                .Where(x => x.WasActiveAtOpen)
                .ToArray(),
            EligibilityMutationPolicy.LateActivationForRosteredVotersOnly => scopedRosterEntries
                .Where(x => x.IsActive)
                .ToArray(),
            _ => Array.Empty<ElectionRosterEntryRecord>(),
        };
    }

    private static bool IsInCurrentDenominator(ElectionRecord election, ElectionRosterEntryRecord rosterEntry)
    {
        if (election.LifecycleState == ElectionLifecycleState.Draft)
        {
            return rosterEntry.IsActive;
        }

        if (!rosterEntry.WasPresentAtOpen)
        {
            return false;
        }

        return election.EligibilityMutationPolicy switch
        {
            EligibilityMutationPolicy.FrozenAtOpen => rosterEntry.WasActiveAtOpen,
            EligibilityMutationPolicy.LateActivationForRosteredVotersOnly => rosterEntry.IsActive,
            _ => false,
        };
    }

    private static string BuildContactValueHint(
        ElectionRosterContactType contactType,
        string contactValue)
    {
        var normalized = contactValue?.Trim() ?? string.Empty;
        return contactType switch
        {
            ElectionRosterContactType.Phone => BuildPhoneHint(normalized),
            ElectionRosterContactType.Email => BuildEmailHint(normalized),
            _ => "Contact on file",
        };
    }

    private static string BuildPhoneHint(string contactValue)
    {
        var digits = new string(contactValue.Where(char.IsDigit).ToArray());
        if (digits.Length < 4)
        {
            return "SMS on file";
        }

        return $"SMS to ***{digits[^4..]}";
    }

    private static string BuildEmailHint(string contactValue)
    {
        var atIndex = contactValue.LastIndexOf('@');
        if (atIndex >= 0 && atIndex < contactValue.Length - 1)
        {
            return $"Email ending @{contactValue[(atIndex + 1)..]}";
        }

        return "Email on file";
    }

    private static string EncodeHash(byte[]? value) =>
        value is null || value.Length == 0
            ? string.Empty
            : Convert.ToBase64String(value);

    private static Timestamp ToProtoTimestamp(DateTime value) =>
        Timestamp.FromDateTime(DateTime.SpecifyKind(value, DateTimeKind.Utc));

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
