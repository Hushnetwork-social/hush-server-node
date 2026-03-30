using HushNetwork.proto;
using HushNode.Caching;
using HushNode.MemPool;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using System.Security.Cryptography;
using System.Text;
using Olimpo.EntityFramework.Persistency;
using Timestamp = Google.Protobuf.WellKnownTypes.Timestamp;

namespace HushNode.Elections.gRPC;

public class ElectionQueryApplicationService : IElectionQueryApplicationService
{
    private readonly IUnitOfWorkProvider<ElectionsDbContext> _unitOfWorkProvider;
    private readonly ElectionCeremonyOptions _ceremonyOptions;
    private readonly IMemPoolService? _memPoolService;
    private readonly IElectionEnvelopeCryptoService? _electionEnvelopeCryptoService;
    private readonly IElectionCastIdempotencyCacheService? _castIdempotencyCacheService;

    public ElectionQueryApplicationService(IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider)
        : this(unitOfWorkProvider, new ElectionCeremonyOptions(), null, null, null)
    {
    }

    public ElectionQueryApplicationService(
        IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider,
        ElectionCeremonyOptions ceremonyOptions)
        : this(unitOfWorkProvider, ceremonyOptions, null, null, null)
    {
    }

    public ElectionQueryApplicationService(
        IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider,
        ElectionCeremonyOptions ceremonyOptions,
        IMemPoolService? memPoolService,
        IElectionEnvelopeCryptoService? electionEnvelopeCryptoService,
        IElectionCastIdempotencyCacheService? castIdempotencyCacheService)
    {
        _unitOfWorkProvider = unitOfWorkProvider;
        _ceremonyOptions = ceremonyOptions;
        _memPoolService = memPoolService;
        _electionEnvelopeCryptoService = electionEnvelopeCryptoService;
        _castIdempotencyCacheService = castIdempotencyCacheService;
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
        var resultArtifacts = await repository.GetResultArtifactsAsync(electionId) ?? Array.Empty<ElectionResultArtifactRecord>();

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
        response.ResultArtifacts.AddRange(resultArtifacts
            .Where(x => x.Visibility == ElectionResultArtifactVisibility.PublicPlaintext)
            .Select(x => x.ToProto()));

        return response;
    }

    public async Task<GetElectionHubViewResponse> GetElectionHubViewAsync(string actorPublicAddress)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var normalizedActorPublicAddress = actorPublicAddress?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedActorPublicAddress))
        {
            return new GetElectionHubViewResponse
            {
                Success = true,
                ErrorMessage = string.Empty,
                ActorPublicAddress = normalizedActorPublicAddress,
                HasAnyElectionRoles = false,
                EmptyStateReason = "Sign in to view election roles in HushVoting!.",
            };
        }

        var ownerElections = await repository.GetElectionsByOwnerAsync(normalizedActorPublicAddress);
        var reportAccessGrants = await repository.GetReportAccessGrantsByActorAsync(normalizedActorPublicAddress);
        var linkedRosterEntries = await repository.GetRosterEntriesByLinkedActorAsync(normalizedActorPublicAddress);
        var acceptedTrusteeInvitations = await repository.GetAcceptedTrusteeInvitationsByActorAsync(normalizedActorPublicAddress);

        var electionIds = ownerElections
            .Select(x => x.ElectionId)
            .Concat(reportAccessGrants.Select(x => x.ElectionId))
            .Concat(linkedRosterEntries.Select(x => x.ElectionId))
            .Concat(acceptedTrusteeInvitations.Select(x => x.ElectionId))
            .Distinct()
            .ToArray();

        if (electionIds.Length == 0)
        {
            return new GetElectionHubViewResponse
            {
                Success = true,
                ErrorMessage = string.Empty,
                ActorPublicAddress = normalizedActorPublicAddress,
                HasAnyElectionRoles = false,
                EmptyStateReason = "No election roles were found for this actor.",
            };
        }

        var elections = await repository.GetElectionsByIdsAsync(electionIds);
        var selfRosterEntryByElectionId = linkedRosterEntries
            .GroupBy(x => x.ElectionId)
            .ToDictionary(
                x => x.Key,
                x => x
                    .OrderByDescending(y => y.LastUpdatedAt)
                    .First());
        var designatedAuditorGrantByElectionId = reportAccessGrants
            .Where(x => x.GrantRole == ElectionReportAccessGrantRole.DesignatedAuditor)
            .GroupBy(x => x.ElectionId)
            .ToDictionary(
                x => x.Key,
                x => x
                    .OrderByDescending(y => y.GrantedAt)
                    .First());
        var acceptedTrusteeElectionIds = acceptedTrusteeInvitations
            .Select(x => x.ElectionId)
            .ToHashSet();

        var electionEntries = new List<ElectionHubEntryView>();
        foreach (var election in elections
                     .OrderBy(ResolveElectionHubSortOrder)
                     .ThenByDescending(x => x.LastUpdatedAt))
        {
            selfRosterEntryByElectionId.TryGetValue(election.ElectionId, out var selfRosterEntry);
            designatedAuditorGrantByElectionId.TryGetValue(election.ElectionId, out var reportAccessGrant);

            var isOwnerAdmin = string.Equals(
                election.OwnerPublicAddress,
                normalizedActorPublicAddress,
                StringComparison.OrdinalIgnoreCase);
            var isTrustee = acceptedTrusteeElectionIds.Contains(election.ElectionId);
            var isVoter = selfRosterEntry is not null;
            var isDesignatedAuditor = reportAccessGrant is not null;
            var participationRecord = selfRosterEntry is null
                ? null
                : await repository.GetParticipationRecordAsync(
                    election.ElectionId,
                    selfRosterEntry.OrganizationVoterId);
            var pendingProposal = isTrustee
                ? await repository.GetPendingGovernedProposalAsync(election.ElectionId)
                : null;
            var pendingApprovals =
                pendingProposal is not null &&
                pendingProposal.ExecutionStatus == ElectionGovernedProposalExecutionStatus.WaitingForApprovals
                    ? await repository.GetGovernedProposalApprovalsAsync(pendingProposal.Id)
                    : Array.Empty<ElectionGovernedProposalApprovalRecord>();
            var suggestedAction = ResolveSuggestedHubAction(
                election,
                normalizedActorPublicAddress,
                selfRosterEntry,
                participationRecord,
                isOwnerAdmin,
                isTrustee,
                isDesignatedAuditor,
                pendingProposal,
                pendingApprovals);

            electionEntries.Add(new ElectionHubEntryView
            {
                Election = election.ToSummaryProto(),
                ActorRoles = new ElectionApplicationRoleFlagsView
                {
                    IsOwnerAdmin = isOwnerAdmin,
                    IsTrustee = isTrustee,
                    IsVoter = isVoter,
                    IsDesignatedAuditor = isDesignatedAuditor,
                },
                SuggestedAction = suggestedAction.Action,
                SuggestedActionReason = suggestedAction.Reason,
                // Pre-link voter discovery still needs a product-level identity-to-roster seam.
                CanClaimIdentity = false,
                CanViewNamedParticipationRoster = isOwnerAdmin || isDesignatedAuditor,
                CanViewReportPackage = isOwnerAdmin || isTrustee || isDesignatedAuditor,
                CanViewParticipantResults = isOwnerAdmin || isTrustee || isVoter || isDesignatedAuditor,
                ClosedProgressStatus = (ElectionClosedProgressStatusProto)(int)election.ClosedProgressStatus,
                HasUnofficialResult = election.UnofficialResultArtifactId.HasValue,
                HasOfficialResult = election.OfficialResultArtifactId.HasValue,
            });
        }

        var response = new GetElectionHubViewResponse
        {
            Success = true,
            ErrorMessage = string.Empty,
            ActorPublicAddress = normalizedActorPublicAddress,
            HasAnyElectionRoles = electionEntries.Count > 0,
            EmptyStateReason = electionEntries.Count == 0
                ? "No election roles were found for this actor."
                : string.Empty,
        };
        response.Elections.AddRange(electionEntries);
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

        var rosterEntries = await repository.GetRosterEntriesAsync(electionId);
        var participationRecords = await repository.GetParticipationRecordsAsync(electionId);
        var activationEvents = await repository.GetEligibilityActivationEventsAsync(electionId);
        var snapshots = await repository.GetEligibilitySnapshotsAsync(electionId);
        var selfRosterEntry = string.IsNullOrWhiteSpace(normalizedActorPublicAddress)
            ? null
            : await repository.GetRosterEntryByLinkedActorAsync(electionId, normalizedActorPublicAddress);
        var reportAccessGrant = string.IsNullOrWhiteSpace(normalizedActorPublicAddress)
            ? null
            : await repository.GetReportAccessGrantAsync(electionId, normalizedActorPublicAddress);
        var actorRole = ResolveEligibilityActorRole(
            election,
            selfRosterEntry,
            reportAccessGrant,
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
            CanClaimIdentity = CanClaimElectionIdentity(
                normalizedActorPublicAddress,
                actorRole == ElectionEligibilityActorRoleProto.EligibilityActorOwner,
                selfRosterEntry),
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

    public async Task<GetElectionVotingViewResponse> GetElectionVotingViewAsync(
        ElectionId electionId,
        string actorPublicAddress,
        string? submissionIdempotencyKey)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var normalizedActorPublicAddress = actorPublicAddress?.Trim() ?? string.Empty;

        var election = await repository.GetElectionAsync(electionId);
        if (election is null)
        {
            return new GetElectionVotingViewResponse
            {
                Success = false,
                ErrorMessage = $"Election {electionId} was not found.",
                ActorPublicAddress = normalizedActorPublicAddress,
                PersonalParticipationStatus = ElectionParticipationStatusProto.ParticipationDidNotVote,
                SubmissionStatus = ElectionVotingSubmissionStatusProto.VotingSubmissionStatusNone,
            };
        }

        var selfRosterEntry = string.IsNullOrWhiteSpace(normalizedActorPublicAddress)
            ? null
            : await repository.GetRosterEntryByLinkedActorAsync(electionId, normalizedActorPublicAddress);
        var participationRecord = selfRosterEntry is null
            ? null
            : await repository.GetParticipationRecordAsync(electionId, selfRosterEntry.OrganizationVoterId);
        var commitmentRegistration = string.IsNullOrWhiteSpace(normalizedActorPublicAddress)
            ? null
            : await repository.GetCommitmentRegistrationByLinkedActorAsync(electionId, normalizedActorPublicAddress);
        var checkoffConsumption = selfRosterEntry is null
            ? null
            : await repository.GetCheckoffConsumptionAsync(electionId, selfRosterEntry.OrganizationVoterId);
        var boundaryArtifacts = await repository.GetBoundaryArtifactsAsync(electionId);
        var openArtifact = ResolveOpenArtifact(election, boundaryArtifacts);

        var response = new GetElectionVotingViewResponse
        {
            Success = true,
            ErrorMessage = string.Empty,
            ActorPublicAddress = normalizedActorPublicAddress,
            Election = election.ToProto(),
            PersonalParticipationStatus = (ElectionParticipationStatusProto)(int)(participationRecord?.ParticipationStatus
                ?? ElectionParticipationStatus.DidNotVote),
            SubmissionStatus = await ResolveSubmissionStatusAsync(repository, electionId, submissionIdempotencyKey),
        };

        if (selfRosterEntry is not null)
        {
            response.SelfRosterEntry = ToEligibilityRosterEntryView(
                election,
                selfRosterEntry,
                participationRecord);
        }

        if (commitmentRegistration is not null)
        {
            response.CommitmentRegistered = true;
            response.CommitmentRegisteredAt = ToProtoTimestamp(commitmentRegistration.RegisteredAt);
            response.HasCommitmentRegisteredAt = true;
        }

        if (checkoffConsumption is not null)
        {
            response.AcceptedAt = ToProtoTimestamp(checkoffConsumption.ConsumedAt);
            response.HasAcceptedAt = true;
            response.AcceptanceId = checkoffConsumption.Id.ToString();
            response.ReceiptId = BuildReceiptId(checkoffConsumption);
            response.ServerProof = BuildReceiptProof(checkoffConsumption);
        }

        if (openArtifact is not null)
        {
            response.OpenArtifactId = openArtifact.Id.ToString();
            response.EligibleSetHash = EncodeHash(openArtifact.FrozenEligibleVoterSetHash);

            if (openArtifact.CeremonySnapshot is not null)
            {
                response.CeremonyVersionId = openArtifact.CeremonySnapshot.CeremonyVersionId.ToString();
                response.DkgProfileId = openArtifact.CeremonySnapshot.ProfileId;
                response.TallyPublicKeyFingerprint = openArtifact.CeremonySnapshot.TallyPublicKeyFingerprint;
            }
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

    public async Task<GetElectionResultViewResponse> GetElectionResultViewAsync(
        ElectionId electionId,
        string actorPublicAddress)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var normalizedActorPublicAddress = actorPublicAddress?.Trim() ?? string.Empty;

        var election = await repository.GetElectionAsync(electionId);
        if (election is null)
        {
            return new GetElectionResultViewResponse
            {
                Success = false,
                ErrorMessage = $"Election {electionId} was not found.",
                ActorPublicAddress = normalizedActorPublicAddress,
            };
        }

        var trusteeInvitations = await repository.GetTrusteeInvitationsAsync(electionId);
        var selfRosterEntry = string.IsNullOrWhiteSpace(normalizedActorPublicAddress)
            ? null
            : await repository.GetRosterEntryByLinkedActorAsync(electionId, normalizedActorPublicAddress);
        var reportAccessGrant = string.IsNullOrWhiteSpace(normalizedActorPublicAddress)
            ? null
            : await repository.GetReportAccessGrantAsync(electionId, normalizedActorPublicAddress);
        var isOwner = !string.IsNullOrWhiteSpace(normalizedActorPublicAddress) &&
            string.Equals(election.OwnerPublicAddress, normalizedActorPublicAddress, StringComparison.OrdinalIgnoreCase);
        var acceptedTrustee = trusteeInvitations.Any(x =>
            x.Status == ElectionTrusteeInvitationStatus.Accepted &&
            string.Equals(x.TrusteeUserAddress, normalizedActorPublicAddress, StringComparison.OrdinalIgnoreCase));
        var isDesignatedAuditor = reportAccessGrant?.GrantRole == ElectionReportAccessGrantRole.DesignatedAuditor;
        var canViewParticipantEncryptedResults =
            !string.IsNullOrWhiteSpace(normalizedActorPublicAddress) &&
            (isOwner ||
             selfRosterEntry is not null ||
             acceptedTrustee ||
             isDesignatedAuditor);
        var canViewReportPackage = !string.IsNullOrWhiteSpace(normalizedActorPublicAddress) &&
            (isOwner || acceptedTrustee || isDesignatedAuditor);
        var canViewRestrictedReportArtifacts = isOwner || isDesignatedAuditor;

        var unofficialResult = await repository.GetResultArtifactAsync(electionId, ElectionResultArtifactKind.Unofficial);
        var officialResult = await repository.GetResultArtifactAsync(electionId, ElectionResultArtifactKind.Official);
        var latestReportPackage = await repository.GetLatestReportPackageAsync(electionId);

        var response = new GetElectionResultViewResponse
        {
            Success = true,
            ErrorMessage = string.Empty,
            ActorPublicAddress = normalizedActorPublicAddress,
            CanViewParticipantEncryptedResults = canViewParticipantEncryptedResults,
            OfficialResultVisibilityPolicy = (OfficialResultVisibilityPolicyProto)(int)election.OfficialResultVisibilityPolicy,
            ClosedProgressStatus = (ElectionClosedProgressStatusProto)(int)election.ClosedProgressStatus,
            CanViewReportPackage = canViewReportPackage,
            CanRetryFailedPackageFinalization =
                isOwner &&
                election.LifecycleState == ElectionLifecycleState.Closed &&
                latestReportPackage?.Status == ElectionReportPackageStatus.GenerationFailed,
        };

        if (unofficialResult is not null && canViewParticipantEncryptedResults)
        {
            response.UnofficialResult = unofficialResult.ToProto();
        }

        if (officialResult is not null &&
            (officialResult.Visibility == ElectionResultArtifactVisibility.PublicPlaintext || canViewParticipantEncryptedResults))
        {
            response.OfficialResult = officialResult.ToProto();
        }

        if (canViewReportPackage && latestReportPackage is not null)
        {
            response.LatestReportPackage = latestReportPackage.ToProto();

            if (latestReportPackage.Status == ElectionReportPackageStatus.Sealed)
            {
                var reportArtifacts = await repository.GetReportArtifactsAsync(latestReportPackage.Id);
                response.VisibleReportArtifacts.AddRange(reportArtifacts
                    .Where(x =>
                        x.AccessScope == ElectionReportArtifactAccessScope.OwnerAuditorTrustee ||
                        canViewRestrictedReportArtifacts)
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.ArtifactKind)
                    .Select(x => x.ToProto()));
            }
        }

        return response;
    }

    public async Task<GetElectionReportAccessGrantsResponse> GetElectionReportAccessGrantsAsync(
        ElectionId electionId,
        string actorPublicAddress)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var normalizedActorPublicAddress = actorPublicAddress?.Trim() ?? string.Empty;

        var election = await repository.GetElectionAsync(electionId);
        if (election is null)
        {
            return new GetElectionReportAccessGrantsResponse
            {
                Success = false,
                ErrorMessage = $"Election {electionId} was not found.",
                ActorPublicAddress = normalizedActorPublicAddress,
            };
        }

        var canManageGrants = !string.IsNullOrWhiteSpace(normalizedActorPublicAddress) &&
            string.Equals(election.OwnerPublicAddress, normalizedActorPublicAddress, StringComparison.OrdinalIgnoreCase);
        var response = new GetElectionReportAccessGrantsResponse
        {
            Success = true,
            ErrorMessage = string.Empty,
            ActorPublicAddress = normalizedActorPublicAddress,
            CanManageGrants = canManageGrants,
            DeniedReason = canManageGrants
                ? string.Empty
                : "Only the election owner can manage designated-auditor grants.",
        };

        if (!canManageGrants)
        {
            return response;
        }

        var grants = await repository.GetReportAccessGrantsAsync(electionId);
        response.Grants.AddRange(grants
            .OrderByDescending(x => x.GrantedAt)
            .ThenBy(x => x.ActorPublicAddress, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.ToProto()));

        return response;
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

    private static int ResolveElectionHubSortOrder(ElectionRecord election) =>
        election.LifecycleState switch
        {
            ElectionLifecycleState.Open => 0,
            ElectionLifecycleState.Draft => 1,
            ElectionLifecycleState.Closed => 2,
            ElectionLifecycleState.Finalized => 3,
            _ => 4,
        };

    private static bool CanClaimElectionIdentity(
        string actorPublicAddress,
        bool isOwner,
        ElectionRosterEntryRecord? selfRosterEntry) =>
        !string.IsNullOrWhiteSpace(actorPublicAddress) &&
        !isOwner &&
        selfRosterEntry is null;

    private static (ElectionHubNextActionHintProto Action, string Reason) ResolveSuggestedHubAction(
        ElectionRecord election,
        string actorPublicAddress,
        ElectionRosterEntryRecord? selfRosterEntry,
        ElectionParticipationRecord? participationRecord,
        bool isOwnerAdmin,
        bool isTrustee,
        bool isDesignatedAuditor,
        ElectionGovernedProposalRecord? pendingProposal,
        IReadOnlyList<ElectionGovernedProposalApprovalRecord> pendingApprovals)
    {
        if (CanLinkedVoterCastBallot(election, selfRosterEntry, participationRecord))
        {
            return (
                ElectionHubNextActionHintProto.ElectionHubActionVoterCastBallot,
                "Cast your ballot while the election remains open.");
        }

        if (CanTrusteeApproveGovernedAction(actorPublicAddress, pendingProposal, pendingApprovals))
        {
            return (
                ElectionHubNextActionHintProto.ElectionHubActionTrusteeApproveGovernedAction,
                pendingProposal!.ActionType switch
                {
                    ElectionGovernedActionType.Open => "A governed open request is awaiting your approval.",
                    ElectionGovernedActionType.Close => "A governed close request is awaiting your approval.",
                    ElectionGovernedActionType.Finalize => "A governed finalization request is awaiting your approval.",
                    _ => "A governed election action is awaiting your approval.",
                });
        }

        if (isOwnerAdmin && election.LifecycleState == ElectionLifecycleState.Draft)
        {
            return (
                ElectionHubNextActionHintProto.ElectionHubActionOwnerManageDraft,
                "Finish the draft details and open the election when ready.");
        }

        if (isOwnerAdmin &&
            election.LifecycleState == ElectionLifecycleState.Closed &&
            !election.OfficialResultArtifactId.HasValue)
        {
            return (
                ElectionHubNextActionHintProto.ElectionHubActionOwnerMonitorClosedProgress,
                "Monitor closed-progress work until the final result is ready.");
        }

        if (isOwnerAdmin &&
            (election.LifecycleState == ElectionLifecycleState.Finalized || election.OfficialResultArtifactId.HasValue))
        {
            return (
                ElectionHubNextActionHintProto.ElectionHubActionOwnerReviewFinalResult,
                "Review the finalized result and published evidence package.");
        }

        if (selfRosterEntry is not null &&
            (election.UnofficialResultArtifactId.HasValue || election.OfficialResultArtifactId.HasValue))
        {
            return (
                ElectionHubNextActionHintProto.ElectionHubActionVoterReviewResult,
                "Review the available election result artifacts.");
        }

        if (isTrustee &&
            (election.UnofficialResultArtifactId.HasValue || election.OfficialResultArtifactId.HasValue))
        {
            return (
                ElectionHubNextActionHintProto.ElectionHubActionTrusteeReviewResult,
                "Review the available election result artifacts.");
        }

        if (isDesignatedAuditor)
        {
            return (
                ElectionHubNextActionHintProto.ElectionHubActionAuditorReviewPackage,
                "Review the election evidence package and auditor-visible artifacts.");
        }

        return (
            ElectionHubNextActionHintProto.ElectionHubActionNone,
            "No immediate action is required for this election.");
    }

    private static bool CanLinkedVoterCastBallot(
        ElectionRecord election,
        ElectionRosterEntryRecord? selfRosterEntry,
        ElectionParticipationRecord? participationRecord) =>
        election.LifecycleState == ElectionLifecycleState.Open &&
        selfRosterEntry is not null &&
        participationRecord is null &&
        IsInCurrentDenominator(election, selfRosterEntry);

    private static bool CanTrusteeApproveGovernedAction(
        string actorPublicAddress,
        ElectionGovernedProposalRecord? pendingProposal,
        IReadOnlyList<ElectionGovernedProposalApprovalRecord> pendingApprovals) =>
        !string.IsNullOrWhiteSpace(actorPublicAddress) &&
        pendingProposal is not null &&
        pendingProposal.ExecutionStatus == ElectionGovernedProposalExecutionStatus.WaitingForApprovals &&
        pendingApprovals.All(x =>
            !string.Equals(x.TrusteeUserAddress, actorPublicAddress, StringComparison.OrdinalIgnoreCase));

    private async Task<ElectionVotingSubmissionStatusProto> ResolveSubmissionStatusAsync(
        IElectionsRepository repository,
        ElectionId electionId,
        string? submissionIdempotencyKey)
    {
        var normalizedSubmissionIdempotencyKey = submissionIdempotencyKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedSubmissionIdempotencyKey))
        {
            return ElectionVotingSubmissionStatusProto.VotingSubmissionStatusNone;
        }

        if (HasPendingCastSubmission(electionId, normalizedSubmissionIdempotencyKey))
        {
            return ElectionVotingSubmissionStatusProto.VotingSubmissionStatusStillProcessing;
        }

        var scopedHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalizedSubmissionIdempotencyKey)));
        if (_castIdempotencyCacheService is not null)
        {
            var cachedMarker = await _castIdempotencyCacheService.ExistsAsync(
                electionId.ToString(),
                scopedHash);
            if (cachedMarker == true)
            {
                return ElectionVotingSubmissionStatusProto.VotingSubmissionStatusAlreadyUsed;
            }
        }

        var committedMarker = await repository.GetCastIdempotencyRecordAsync(electionId, scopedHash);
        if (committedMarker is not null && _castIdempotencyCacheService is not null)
        {
            await _castIdempotencyCacheService.SetAsync(
                electionId.ToString(),
                scopedHash);
        }

        return committedMarker is null
            ? ElectionVotingSubmissionStatusProto.VotingSubmissionStatusNone
            : ElectionVotingSubmissionStatusProto.VotingSubmissionStatusAlreadyUsed;
    }

    private bool HasPendingCastSubmission(
        ElectionId electionId,
        string submissionIdempotencyKey)
    {
        if (_memPoolService is null || _electionEnvelopeCryptoService is null)
        {
            return false;
        }

        foreach (var transaction in _memPoolService.PeekPendingValidatedTransactions())
        {
            var decryptedEnvelope = _electionEnvelopeCryptoService.TryDecryptValidated(transaction);
            if (decryptedEnvelope is null ||
                decryptedEnvelope.ActionType != EncryptedElectionEnvelopeActionTypes.AcceptBallotCast ||
                decryptedEnvelope.Transaction.Payload.ElectionId != electionId)
            {
                continue;
            }

            var acceptCastAction = decryptedEnvelope.DeserializeAction<AcceptElectionBallotCastActionPayload>();
            if (acceptCastAction is null)
            {
                continue;
            }

            if (string.Equals(
                    acceptCastAction.IdempotencyKey?.Trim(),
                    submissionIdempotencyKey,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static ElectionBoundaryArtifactRecord? ResolveOpenArtifact(
        ElectionRecord election,
        IReadOnlyList<ElectionBoundaryArtifactRecord> boundaryArtifacts)
    {
        if (election.OpenArtifactId.HasValue)
        {
            return boundaryArtifacts.FirstOrDefault(x => x.Id == election.OpenArtifactId.Value);
        }

        return boundaryArtifacts
            .Where(x => x.ArtifactType == ElectionBoundaryArtifactType.Open)
            .OrderByDescending(x => x.RecordedAt)
            .FirstOrDefault();
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
        ElectionRosterEntryRecord? selfRosterEntry,
        ElectionReportAccessGrantRecord? reportAccessGrant,
        string actorPublicAddress)
    {
        if (!string.IsNullOrWhiteSpace(actorPublicAddress) &&
            string.Equals(election.OwnerPublicAddress, actorPublicAddress, StringComparison.OrdinalIgnoreCase))
        {
            return ElectionEligibilityActorRoleProto.EligibilityActorOwner;
        }

        if (reportAccessGrant?.GrantRole == ElectionReportAccessGrantRole.DesignatedAuditor)
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

    private static string BuildReceiptId(ElectionCheckoffConsumptionRecord checkoffConsumption)
    {
        var receiptSeed = string.Join(
            "|",
            checkoffConsumption.ElectionId,
            checkoffConsumption.Id,
            checkoffConsumption.ConsumedAt.ToUniversalTime().ToString("O"),
            checkoffConsumption.ParticipationStatus);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(receiptSeed));
        return $"rcpt-{Convert.ToHexString(hash)[..24].ToLowerInvariant()}";
    }

    private static string BuildReceiptProof(ElectionCheckoffConsumptionRecord checkoffConsumption)
    {
        var proofSeed = string.Join(
            "|",
            BuildReceiptId(checkoffConsumption),
            checkoffConsumption.ElectionId,
            checkoffConsumption.Id,
            checkoffConsumption.ConsumedAt.ToUniversalTime().ToString("O"),
            checkoffConsumption.ParticipationStatus);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(proofSeed))).ToLowerInvariant();
    }

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
