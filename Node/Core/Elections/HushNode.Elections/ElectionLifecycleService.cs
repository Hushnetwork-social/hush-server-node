using System.Data;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Microsoft.Extensions.Logging;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections;

public class ElectionLifecycleService(
    IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider,
    ILogger<ElectionLifecycleService> logger)
    : IElectionLifecycleService
{
    private readonly IUnitOfWorkProvider<ElectionsDbContext> _unitOfWorkProvider = unitOfWorkProvider;
    private readonly ILogger<ElectionLifecycleService> _logger = logger;

    public async Task<ElectionCommandResult> CreateDraftAsync(CreateElectionDraftRequest request)
    {
        if (!string.Equals(request.OwnerPublicAddress, request.ActorPublicAddress, StringComparison.Ordinal))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Forbidden,
                "Only the owner can create the draft election.");
        }

        var validationErrors = ValidateDraftRequest(request.ActorPublicAddress, request.SnapshotReason, request.Draft);
        if (validationErrors.Count > 0)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Draft election validation failed.",
                validationErrors);
        }

        var election = ElectionModelFactory.CreateDraftRecord(
            electionId: ElectionId.NewElectionId,
            title: request.Draft.Title,
            shortDescription: request.Draft.ShortDescription,
            ownerPublicAddress: request.OwnerPublicAddress,
            externalReferenceCode: request.Draft.ExternalReferenceCode,
            electionClass: request.Draft.ElectionClass,
            bindingStatus: request.Draft.BindingStatus,
            governanceMode: request.Draft.GovernanceMode,
            disclosureMode: request.Draft.DisclosureMode,
            participationPrivacyMode: request.Draft.ParticipationPrivacyMode,
            voteUpdatePolicy: request.Draft.VoteUpdatePolicy,
            eligibilitySourceType: request.Draft.EligibilitySourceType,
            eligibilityMutationPolicy: request.Draft.EligibilityMutationPolicy,
            outcomeRule: request.Draft.OutcomeRule,
            approvedClientApplications: NormalizeApprovedApplications(request.Draft.ApprovedClientApplications),
            protocolOmegaVersion: request.Draft.ProtocolOmegaVersion,
            reportingPolicy: request.Draft.ReportingPolicy,
            reviewWindowPolicy: request.Draft.ReviewWindowPolicy,
            ownerOptions: NormalizeOwnerOptions(request.Draft.OwnerOptions),
            acknowledgedWarningCodes: NormalizeWarningCodes(request.Draft.AcknowledgedWarningCodes),
            requiredApprovalCount: request.Draft.RequiredApprovalCount);

        var snapshot = ElectionModelFactory.CreateDraftSnapshot(
            election,
            request.SnapshotReason,
            request.ActorPublicAddress);
        var warningAcknowledgements = CreateWarningAcknowledgementsForRevision(
            election.ElectionId,
            election.CurrentDraftRevision,
            request.ActorPublicAddress,
            election.AcknowledgedWarningCodes);

        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();

        await repository.SaveElectionAsync(election);
        await repository.SaveDraftSnapshotAsync(snapshot);
        foreach (var acknowledgement in warningAcknowledgements)
        {
            await repository.SaveWarningAcknowledgementAsync(acknowledgement);
        }

        await unitOfWork.CommitAsync();

        _logger.LogInformation(
            "[ElectionLifecycleService] Created draft election {ElectionId} at revision {Revision}",
            election.ElectionId,
            election.CurrentDraftRevision);

        return ElectionCommandResult.Success(election, draftSnapshot: snapshot);
    }

    public async Task<ElectionCommandResult> UpdateDraftAsync(UpdateElectionDraftRequest request)
    {
        var validationErrors = ValidateDraftRequest(request.ActorPublicAddress, request.SnapshotReason, request.Draft);
        if (validationErrors.Count > 0)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Draft election validation failed.",
                validationErrors);
        }

        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var existing = await repository.GetElectionForUpdateAsync(request.ElectionId);

        if (existing is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Election {request.ElectionId} was not found.");
        }

        if (existing.LifecycleState != ElectionLifecycleState.Draft)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.InvalidState,
                "Immutable FEAT-094 policy cannot be edited after the election opens.");
        }

        if (!string.Equals(existing.OwnerPublicAddress, request.ActorPublicAddress, StringComparison.Ordinal))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Forbidden,
                "Only the owner can edit a draft election.");
        }

        var updated = ElectionModelFactory.CreateDraftRecord(
            electionId: existing.ElectionId,
            title: request.Draft.Title,
            shortDescription: request.Draft.ShortDescription,
            ownerPublicAddress: existing.OwnerPublicAddress,
            externalReferenceCode: request.Draft.ExternalReferenceCode,
            electionClass: request.Draft.ElectionClass,
            bindingStatus: request.Draft.BindingStatus,
            governanceMode: request.Draft.GovernanceMode,
            disclosureMode: request.Draft.DisclosureMode,
            participationPrivacyMode: request.Draft.ParticipationPrivacyMode,
            voteUpdatePolicy: request.Draft.VoteUpdatePolicy,
            eligibilitySourceType: request.Draft.EligibilitySourceType,
            eligibilityMutationPolicy: request.Draft.EligibilityMutationPolicy,
            outcomeRule: request.Draft.OutcomeRule,
            approvedClientApplications: NormalizeApprovedApplications(request.Draft.ApprovedClientApplications),
            protocolOmegaVersion: request.Draft.ProtocolOmegaVersion,
            reportingPolicy: request.Draft.ReportingPolicy,
            reviewWindowPolicy: request.Draft.ReviewWindowPolicy,
            ownerOptions: NormalizeOwnerOptions(request.Draft.OwnerOptions),
            acknowledgedWarningCodes: NormalizeWarningCodes(request.Draft.AcknowledgedWarningCodes),
            currentDraftRevision: existing.CurrentDraftRevision + 1,
            requiredApprovalCount: request.Draft.RequiredApprovalCount,
            createdAt: existing.CreatedAt) with
        {
            LastUpdatedAt = DateTime.UtcNow,
        };

        var snapshot = ElectionModelFactory.CreateDraftSnapshot(
            updated,
            request.SnapshotReason,
            request.ActorPublicAddress);
        var warningAcknowledgements = CreateWarningAcknowledgementsForRevision(
            updated.ElectionId,
            updated.CurrentDraftRevision,
            request.ActorPublicAddress,
            updated.AcknowledgedWarningCodes);

        await repository.SaveElectionAsync(updated);
        await repository.SaveDraftSnapshotAsync(snapshot);
        foreach (var acknowledgement in warningAcknowledgements)
        {
            await repository.SaveWarningAcknowledgementAsync(acknowledgement);
        }

        await unitOfWork.CommitAsync();

        _logger.LogInformation(
            "[ElectionLifecycleService] Updated draft election {ElectionId} to revision {Revision}",
            updated.ElectionId,
            updated.CurrentDraftRevision);

        return ElectionCommandResult.Success(updated, draftSnapshot: snapshot);
    }

    public async Task<ElectionCommandResult> InviteTrusteeAsync(InviteElectionTrusteeRequest request)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionForUpdateAsync(request.ElectionId);

        if (election is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Election {request.ElectionId} was not found.");
        }

        var phaseResult = ValidateDraftOwnershipAndTrusteeMode(election, request.ActorPublicAddress);
        if (phaseResult is not null)
        {
            return phaseResult;
        }

        var invitations = await repository.GetTrusteeInvitationsAsync(request.ElectionId);
        var existing = invitations.FirstOrDefault(x =>
            string.Equals(x.TrusteeUserAddress, request.TrusteeUserAddress, StringComparison.OrdinalIgnoreCase) &&
            (x.Status == ElectionTrusteeInvitationStatus.Pending || x.Status == ElectionTrusteeInvitationStatus.Accepted));

        if (existing is not null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Conflict,
                $"Trustee {request.TrusteeUserAddress} already has an active invitation state.");
        }

        var invitation = ElectionModelFactory.CreateTrusteeInvitation(
            request.ElectionId,
            request.TrusteeUserAddress,
            request.TrusteeDisplayName,
            request.ActorPublicAddress,
            election.CurrentDraftRevision);

        await repository.SaveTrusteeInvitationAsync(invitation);
        await unitOfWork.CommitAsync();

        return ElectionCommandResult.Success(election, trusteeInvitation: invitation);
    }

    public Task<ElectionCommandResult> AcceptTrusteeInvitationAsync(ResolveElectionTrusteeInvitationRequest request) =>
        ResolveTrusteeInvitationAsync(
            request,
            ownerOnly: false,
            transition: (invitation, draftRevision, lifecycleState) =>
                invitation.Accept(DateTime.UtcNow, draftRevision, lifecycleState));

    public Task<ElectionCommandResult> RejectTrusteeInvitationAsync(ResolveElectionTrusteeInvitationRequest request) =>
        ResolveTrusteeInvitationAsync(
            request,
            ownerOnly: false,
            transition: (invitation, draftRevision, lifecycleState) =>
                invitation.Reject(DateTime.UtcNow, draftRevision, lifecycleState));

    public Task<ElectionCommandResult> RevokeTrusteeInvitationAsync(ResolveElectionTrusteeInvitationRequest request) =>
        ResolveTrusteeInvitationAsync(
            request,
            ownerOnly: true,
            transition: (invitation, draftRevision, lifecycleState) =>
                invitation.Revoke(DateTime.UtcNow, draftRevision, lifecycleState));

    public async Task<ElectionOpenValidationResult> EvaluateOpenReadinessAsync(EvaluateElectionOpenReadinessRequest request)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionAsync(request.ElectionId);

        if (election is null)
        {
            return ElectionOpenValidationResult.NotReady(
                [$"Election {request.ElectionId} was not found."],
                Array.Empty<ElectionWarningCode>(),
                Array.Empty<ElectionWarningCode>());
        }

        var invitations = await repository.GetTrusteeInvitationsAsync(request.ElectionId);
        var warningAcknowledgements = await repository.GetWarningAcknowledgementsAsync(request.ElectionId);

        return EvaluateOpenReadiness(
            election,
            invitations,
            warningAcknowledgements,
            request.RequiredWarningCodes);
    }

    public async Task<ElectionCommandResult> OpenElectionAsync(OpenElectionRequest request)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionForUpdateAsync(request.ElectionId);

        if (election is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Election {request.ElectionId} was not found.");
        }

        if (!string.Equals(election.OwnerPublicAddress, request.ActorPublicAddress, StringComparison.Ordinal))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Forbidden,
                "Only the owner can open the election.");
        }

        var invitations = await repository.GetTrusteeInvitationsAsync(request.ElectionId);
        var warningAcknowledgements = await repository.GetWarningAcknowledgementsAsync(request.ElectionId);
        var readiness = EvaluateOpenReadiness(
            election,
            invitations,
            warningAcknowledgements,
            request.RequiredWarningCodes);

        if (!readiness.IsReadyToOpen)
        {
            return ElectionCommandResult.Failure(
                readiness.ValidationErrors.Any(x => x.Contains("FEAT-096", StringComparison.Ordinal))
                    ? ElectionCommandErrorCode.DependencyBlocked
                    : ElectionCommandErrorCode.ValidationFailed,
                "Election cannot be opened.",
                readiness.ValidationErrors);
        }

        var openedAt = DateTime.UtcNow;
        var artifact = ElectionModelFactory.CreateBoundaryArtifact(
            artifactType: ElectionBoundaryArtifactType.Open,
            election: election,
            recordedByPublicAddress: request.ActorPublicAddress,
            recordedAt: openedAt,
            trusteeSnapshot: null,
            frozenEligibleVoterSetHash: request.FrozenEligibleVoterSetHash,
            trusteePolicyExecutionReference: request.TrusteePolicyExecutionReference,
            reportingPolicyExecutionReference: request.ReportingPolicyExecutionReference,
            reviewWindowExecutionReference: request.ReviewWindowExecutionReference);

        var openedElection = election with
        {
            LifecycleState = ElectionLifecycleState.Open,
            LastUpdatedAt = openedAt,
            OpenedAt = openedAt,
            OpenArtifactId = artifact.Id,
        };

        await repository.SaveBoundaryArtifactAsync(artifact);
        await repository.SaveElectionAsync(openedElection);
        await unitOfWork.CommitAsync();

        return ElectionCommandResult.Success(openedElection, boundaryArtifact: artifact);
    }

    public Task<ElectionCommandResult> CloseElectionAsync(CloseElectionRequest request) =>
        TransitionElectionAsync(
            request.ElectionId,
            request.ActorPublicAddress,
            ElectionLifecycleState.Open,
            ElectionBoundaryArtifactType.Close,
            request.AcceptedBallotSetHash,
            request.FinalEncryptedTallyHash);

    public Task<ElectionCommandResult> FinalizeElectionAsync(FinalizeElectionRequest request) =>
        TransitionElectionAsync(
            request.ElectionId,
            request.ActorPublicAddress,
            ElectionLifecycleState.Closed,
            ElectionBoundaryArtifactType.Finalize,
            request.AcceptedBallotSetHash,
            request.FinalEncryptedTallyHash);

    private Task<ElectionCommandResult> ResolveTrusteeInvitationAsync(
        ResolveElectionTrusteeInvitationRequest request,
        bool ownerOnly,
        Func<ElectionTrusteeInvitationRecord, int, ElectionLifecycleState, ElectionTrusteeInvitationRecord> transition)
    {
        return ResolveTrusteeInvitationInternalAsync(request, ownerOnly, transition);
    }

    private Task<ElectionCommandResult> TransitionElectionAsync(
        ElectionId electionId,
        string actorPublicAddress,
        ElectionLifecycleState expectedState,
        ElectionBoundaryArtifactType artifactType,
        byte[]? acceptedBallotSetHash,
        byte[]? finalEncryptedTallyHash)
    {
        return TransitionElectionInternalAsync(
            electionId,
            actorPublicAddress,
            expectedState,
            artifactType,
            acceptedBallotSetHash,
            finalEncryptedTallyHash);
    }

    private static ElectionRecord ApplyLifecycleTransition(
        ElectionRecord election,
        ElectionBoundaryArtifactRecord artifact,
        DateTime transitionTime) =>
        artifact.ArtifactType switch
        {
            ElectionBoundaryArtifactType.Close => election with
            {
                LifecycleState = ElectionLifecycleState.Closed,
                LastUpdatedAt = transitionTime,
                ClosedAt = transitionTime,
                CloseArtifactId = artifact.Id,
            },
            ElectionBoundaryArtifactType.Finalize => election with
            {
                LifecycleState = ElectionLifecycleState.Finalized,
                LastUpdatedAt = transitionTime,
                FinalizedAt = transitionTime,
                FinalizeArtifactId = artifact.Id,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(artifact), artifact.ArtifactType, "Unsupported lifecycle transition artifact."),
        };

    private static ElectionCommandResult? ValidateDraftOwnershipAndTrusteeMode(ElectionRecord election, string actorPublicAddress)
    {
        if (election.LifecycleState != ElectionLifecycleState.Draft)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.InvalidState,
                "Trustee draft setup can only be changed while the election remains in draft.");
        }

        if (!string.Equals(election.OwnerPublicAddress, actorPublicAddress, StringComparison.Ordinal))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Forbidden,
                "Only the owner can manage trustee draft setup.");
        }

        if (election.GovernanceMode != ElectionGovernanceMode.TrusteeThreshold)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotSupported,
                "Trustee invitations are only supported for trustee-threshold elections.");
        }

        return null;
    }

    private static ElectionOpenValidationResult EvaluateOpenReadiness(
        ElectionRecord election,
        IReadOnlyList<ElectionTrusteeInvitationRecord> invitations,
        IReadOnlyList<ElectionWarningAcknowledgementRecord> warningAcknowledgements,
        IReadOnlyList<ElectionWarningCode>? requestedWarnings)
    {
        var errors = new List<string>();
        var requiredWarnings = NormalizeWarningCodes(requestedWarnings).ToList();
        var nonBlankOptions = election.Options.Where(x => !x.IsBlankOption).ToArray();

        if (election.LifecycleState != ElectionLifecycleState.Draft)
        {
            errors.Add("Only draft elections can be opened.");
        }

        if (nonBlankOptions.Length < 2)
        {
            errors.Add("At least two non-blank options are required before opening the election.");
        }

        if (election.OutcomeRule.Kind == OutcomeRuleKind.PassFail && nonBlankOptions.Length != 2)
        {
            errors.Add("Pass/fail elections require exactly two non-blank options before opening.");
        }

        if (election.GovernanceMode == ElectionGovernanceMode.AdminOnly &&
            election.ReviewWindowPolicy != ReviewWindowPolicy.NoReviewWindow)
        {
            errors.Add("Admin-only elections must use the no-review-window policy in FEAT-094.");
        }

        var acceptedTrustees = invitations
            .Where(x => x.Status == ElectionTrusteeInvitationStatus.Accepted)
            .Select(x => new ElectionTrusteeReference(x.TrusteeUserAddress, x.TrusteeDisplayName))
            .ToArray();
        var pendingTrustees = invitations.Where(x => x.Status == ElectionTrusteeInvitationStatus.Pending).ToArray();

        if (election.GovernanceMode == ElectionGovernanceMode.TrusteeThreshold)
        {
            if (pendingTrustees.Length > 0)
            {
                errors.Add("Trustee-threshold elections cannot open while trustee invitations remain pending.");
            }

            if (!election.RequiredApprovalCount.HasValue || acceptedTrustees.Length < election.RequiredApprovalCount.Value)
            {
                errors.Add("Trustee-threshold elections require enough accepted trustees to satisfy the required approval count before open.");
            }

            if (election.RequiredApprovalCount.HasValue && acceptedTrustees.Length == election.RequiredApprovalCount.Value)
            {
                requiredWarnings.Add(ElectionWarningCode.AllTrusteesRequiredFragility);
            }

            errors.Add("Trustee-threshold elections cannot open until FEAT-096 provides the governed approval workflow.");
        }

        requiredWarnings = NormalizeWarningCodes(requiredWarnings).ToList();
        var currentRevisionAcknowledgements = warningAcknowledgements
            .Where(x => x.DraftRevision == election.CurrentDraftRevision)
            .Select(x => x.WarningCode)
            .Distinct()
            .ToHashSet();
        var electionWarnings = election.AcknowledgedWarningCodes.ToHashSet();

        var missingWarnings = requiredWarnings
            .Where(x => !electionWarnings.Contains(x) || !currentRevisionAcknowledgements.Contains(x))
            .Distinct()
            .ToArray();

        foreach (var warning in missingWarnings)
        {
            errors.Add($"Required warning acknowledgement is missing for {warning}.");
        }

        return errors.Count == 0
            ? ElectionOpenValidationResult.Ready(requiredWarnings)
            : ElectionOpenValidationResult.NotReady(errors, requiredWarnings, missingWarnings);
    }

    private static List<string> ValidateDraftRequest(
        string actorPublicAddress,
        string snapshotReason,
        ElectionDraftSpecification draft)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(actorPublicAddress))
        {
            errors.Add("Actor public address is required.");
        }

        if (string.IsNullOrWhiteSpace(snapshotReason))
        {
            errors.Add("Snapshot reason is required.");
        }

        if (string.IsNullOrWhiteSpace(draft.Title))
        {
            errors.Add("Election title is required.");
        }

        if (string.IsNullOrWhiteSpace(draft.ProtocolOmegaVersion))
        {
            errors.Add("Protocol Omega version is required.");
        }

        if (draft.ElectionClass != ElectionClass.OrganizationalRemoteVoting)
        {
            errors.Add("FEAT-094 only supports organizational remote voting elections.");
        }

        if (draft.DisclosureMode != ElectionDisclosureMode.FinalResultsOnly)
        {
            errors.Add("FEAT-094 only supports the final-results-only disclosure mode.");
        }

        if (draft.ParticipationPrivacyMode != ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice)
        {
            errors.Add("FEAT-094 only supports the phase-one participation privacy mode.");
        }

        if (draft.VoteUpdatePolicy != VoteUpdatePolicy.SingleSubmissionOnly)
        {
            errors.Add("FEAT-094 only supports the single-submission-only vote update policy.");
        }

        if (draft.EligibilitySourceType != EligibilitySourceType.OrganizationImportedRoster)
        {
            errors.Add("FEAT-094 only supports the organization-imported-roster eligibility source.");
        }

        if (draft.EligibilityMutationPolicy != EligibilityMutationPolicy.FrozenAtOpen)
        {
            errors.Add("FEAT-094 only supports the frozen-at-open eligibility mutation policy.");
        }

        if (draft.ReportingPolicy != ReportingPolicy.DefaultPhaseOnePackage)
        {
            errors.Add("FEAT-094 only supports the default phase-one reporting policy.");
        }

        if (draft.GovernanceMode == ElectionGovernanceMode.AdminOnly && draft.RequiredApprovalCount.HasValue)
        {
            errors.Add("Admin-only elections must not set a required approval count.");
        }

        if (draft.GovernanceMode == ElectionGovernanceMode.TrusteeThreshold &&
            (!draft.RequiredApprovalCount.HasValue || draft.RequiredApprovalCount.Value < 1))
        {
            errors.Add("Trustee-threshold elections require a required approval count of at least 1.");
        }

        errors.AddRange(ValidateOutcomeRule(draft.OutcomeRule));
        errors.AddRange(ValidateOwnerOptions(draft.OwnerOptions));

        return errors;
    }

    private static IReadOnlyList<string> ValidateOutcomeRule(OutcomeRuleDefinition outcomeRule)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(outcomeRule.TemplateKey))
        {
            errors.Add("Outcome rule template key is required.");
        }

        if (string.IsNullOrWhiteSpace(outcomeRule.TieResolutionRule))
        {
            errors.Add("Outcome rule tie-resolution policy is required.");
        }

        if (string.IsNullOrWhiteSpace(outcomeRule.CalculationBasis))
        {
            errors.Add("Outcome rule calculation basis is required.");
        }

        switch (outcomeRule.Kind)
        {
            case OutcomeRuleKind.SingleWinner:
                if (outcomeRule.SeatCount != 1)
                {
                    errors.Add("Single-winner elections must use seat count 1.");
                }
                break;

            case OutcomeRuleKind.PassFail:
                if (outcomeRule.SeatCount != 1)
                {
                    errors.Add("Pass/fail elections must use seat count 1.");
                }
                if (!outcomeRule.BlankVoteExcludedFromThresholdDenominator)
                {
                    errors.Add("Pass/fail elections must exclude blank votes from the threshold denominator.");
                }
                break;

            default:
                errors.Add("FEAT-094 does not support this outcome rule kind.");
                break;
        }

        if (!outcomeRule.BlankVoteCountsForTurnout)
        {
            errors.Add("Blank vote turnout accounting must remain enabled in FEAT-094.");
        }

        if (!outcomeRule.BlankVoteExcludedFromWinnerSelection)
        {
            errors.Add("Blank vote must remain excluded from winner selection in FEAT-094.");
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateOwnerOptions(IReadOnlyList<ElectionOptionDefinition> ownerOptions)
    {
        var errors = new List<string>();
        if (ownerOptions is null)
        {
            errors.Add("Owner options are required.");
            return errors;
        }

        foreach (var option in ownerOptions)
        {
            if (string.IsNullOrWhiteSpace(option.OptionId))
            {
                errors.Add("Each election option must have a stable option id.");
            }

            if (string.IsNullOrWhiteSpace(option.DisplayLabel))
            {
                errors.Add("Each election option must have a display label.");
            }

            if (option.BallotOrder < 0)
            {
                errors.Add("Election option ballot order must be zero or greater.");
            }

            if (option.IsBlankOption)
            {
                errors.Add("Owner options must not mark themselves as the reserved blank vote option.");
            }

            if (string.Equals(option.OptionId, ElectionOptionDefinition.ReservedBlankOptionId, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Owner options must not reuse the reserved blank option id.");
            }
        }

        if (ownerOptions.GroupBy(x => x.OptionId, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
        {
            errors.Add("Election option ids must be unique.");
        }

        if (ownerOptions.GroupBy(x => x.BallotOrder).Any(x => x.Count() > 1))
        {
            errors.Add("Election option ballot order must be unique.");
        }

        return errors;
    }

    private static IReadOnlyList<ElectionOptionDefinition> NormalizeOwnerOptions(IReadOnlyList<ElectionOptionDefinition> ownerOptions) =>
        ownerOptions
            .Select(x => new ElectionOptionDefinition(
                x.OptionId,
                x.DisplayLabel,
                x.ShortDescription,
                x.BallotOrder,
                x.IsBlankOption))
            .ToArray();

    private static IReadOnlyList<ApprovedClientApplicationRecord> NormalizeApprovedApplications(IReadOnlyList<ApprovedClientApplicationRecord> applications) =>
        applications
            .Select(x => new ApprovedClientApplicationRecord(x.ApplicationId, x.Version))
            .ToArray();

    private static IReadOnlyList<ElectionWarningCode> NormalizeWarningCodes(IReadOnlyList<ElectionWarningCode>? warningCodes) =>
        warningCodes is null
            ? Array.Empty<ElectionWarningCode>()
            : warningCodes
                .Distinct()
                .OrderBy(x => (int)x)
                .ToArray();

    private static IReadOnlyList<ElectionWarningAcknowledgementRecord> CreateWarningAcknowledgementsForRevision(
        ElectionId electionId,
        int draftRevision,
        string actorPublicAddress,
        IReadOnlyList<ElectionWarningCode> warningCodes) =>
        warningCodes
            .Select(x => ElectionModelFactory.CreateWarningAcknowledgement(
                electionId,
                x,
                draftRevision,
                actorPublicAddress))
            .ToArray();

    private async Task<ElectionCommandResult> ResolveTrusteeInvitationInternalAsync(
        ResolveElectionTrusteeInvitationRequest request,
        bool ownerOnly,
        Func<ElectionTrusteeInvitationRecord, int, ElectionLifecycleState, ElectionTrusteeInvitationRecord> transition)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionForUpdateAsync(request.ElectionId);

        if (election is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Election {request.ElectionId} was not found.");
        }

        var invitation = await repository.GetTrusteeInvitationAsync(request.InvitationId);
        if (invitation is null || invitation.ElectionId != request.ElectionId)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Trustee invitation {request.InvitationId} was not found for election {request.ElectionId}.");
        }

        var authorizedActor = ownerOnly ? election.OwnerPublicAddress : invitation.TrusteeUserAddress;
        if (!string.Equals(authorizedActor, request.ActorPublicAddress, StringComparison.Ordinal))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Forbidden,
                ownerOnly
                    ? "Only the owner can revoke a trustee invitation."
                    : "Only the invited trustee can resolve this invitation.");
        }

        if (election.LifecycleState != ElectionLifecycleState.Draft)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.InvalidState,
                "Trustee invitations can only be resolved while the election remains in draft.");
        }

        var updated = transition(invitation, election.CurrentDraftRevision, election.LifecycleState);
        await repository.UpdateTrusteeInvitationAsync(updated);
        await unitOfWork.CommitAsync();

        return ElectionCommandResult.Success(election, trusteeInvitation: updated);
    }

    private async Task<ElectionCommandResult> TransitionElectionInternalAsync(
        ElectionId electionId,
        string actorPublicAddress,
        ElectionLifecycleState expectedState,
        ElectionBoundaryArtifactType artifactType,
        byte[]? acceptedBallotSetHash,
        byte[]? finalEncryptedTallyHash)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionForUpdateAsync(electionId);

        if (election is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Election {electionId} was not found.");
        }

        if (!string.Equals(election.OwnerPublicAddress, actorPublicAddress, StringComparison.Ordinal))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Forbidden,
                $"Only the owner can {artifactType.ToString().ToLowerInvariant()} the election.");
        }

        if (election.LifecycleState != expectedState)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.InvalidState,
                artifactType switch
                {
                    ElectionBoundaryArtifactType.Close => "Election close is only allowed from the open state.",
                    ElectionBoundaryArtifactType.Finalize => "Election finalize is only allowed from the closed state.",
                    _ => "Unsupported election transition.",
                });
        }

        var transitionTime = DateTime.UtcNow;
        var artifact = ElectionModelFactory.CreateBoundaryArtifact(
            artifactType,
            election,
            actorPublicAddress,
            recordedAt: transitionTime,
            acceptedBallotSetHash: acceptedBallotSetHash,
            finalEncryptedTallyHash: finalEncryptedTallyHash);

        var updatedElection = ApplyLifecycleTransition(election, artifact, transitionTime);

        await repository.SaveBoundaryArtifactAsync(artifact);
        await repository.SaveElectionAsync(updatedElection);
        await unitOfWork.CommitAsync();

        return ElectionCommandResult.Success(updatedElection, boundaryArtifact: artifact);
    }
}
