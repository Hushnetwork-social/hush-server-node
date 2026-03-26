using HushShared.Elections.Model;

namespace HushNode.Elections;

public interface IElectionLifecycleService
{
    Task<ElectionCommandResult> CreateDraftAsync(CreateElectionDraftRequest request);

    Task<ElectionCommandResult> UpdateDraftAsync(UpdateElectionDraftRequest request);

    Task<ElectionCommandResult> InviteTrusteeAsync(InviteElectionTrusteeRequest request);

    Task<ElectionCommandResult> AcceptTrusteeInvitationAsync(ResolveElectionTrusteeInvitationRequest request);

    Task<ElectionCommandResult> RejectTrusteeInvitationAsync(ResolveElectionTrusteeInvitationRequest request);

    Task<ElectionCommandResult> RevokeTrusteeInvitationAsync(ResolveElectionTrusteeInvitationRequest request);

    Task<ElectionOpenValidationResult> EvaluateOpenReadinessAsync(EvaluateElectionOpenReadinessRequest request);

    Task<ElectionCommandResult> StartGovernedProposalAsync(StartElectionGovernedProposalRequest request);

    Task<ElectionCommandResult> ApproveGovernedProposalAsync(ApproveElectionGovernedProposalRequest request);

    Task<ElectionCommandResult> RetryGovernedProposalExecutionAsync(RetryElectionGovernedProposalExecutionRequest request);

    Task<ElectionCommandResult> OpenElectionAsync(OpenElectionRequest request);

    Task<ElectionCommandResult> CloseElectionAsync(CloseElectionRequest request);

    Task<ElectionCommandResult> FinalizeElectionAsync(FinalizeElectionRequest request);
}

public record ElectionDraftSpecification(
    string Title,
    string? ShortDescription,
    string? ExternalReferenceCode,
    ElectionClass ElectionClass,
    ElectionBindingStatus BindingStatus,
    ElectionGovernanceMode GovernanceMode,
    ElectionDisclosureMode DisclosureMode,
    ParticipationPrivacyMode ParticipationPrivacyMode,
    VoteUpdatePolicy VoteUpdatePolicy,
    EligibilitySourceType EligibilitySourceType,
    EligibilityMutationPolicy EligibilityMutationPolicy,
    OutcomeRuleDefinition OutcomeRule,
    IReadOnlyList<ApprovedClientApplicationRecord> ApprovedClientApplications,
    string ProtocolOmegaVersion,
    ReportingPolicy ReportingPolicy,
    ReviewWindowPolicy ReviewWindowPolicy,
    IReadOnlyList<ElectionOptionDefinition> OwnerOptions,
    IReadOnlyList<ElectionWarningCode>? AcknowledgedWarningCodes = null,
    int? RequiredApprovalCount = null);

public record CreateElectionDraftRequest(
    string OwnerPublicAddress,
    string ActorPublicAddress,
    string SnapshotReason,
    ElectionDraftSpecification Draft);

public record UpdateElectionDraftRequest(
    ElectionId ElectionId,
    string ActorPublicAddress,
    string SnapshotReason,
    ElectionDraftSpecification Draft);

public record InviteElectionTrusteeRequest(
    ElectionId ElectionId,
    string ActorPublicAddress,
    string TrusteeUserAddress,
    string? TrusteeDisplayName);

public record ResolveElectionTrusteeInvitationRequest(
    ElectionId ElectionId,
    Guid InvitationId,
    string ActorPublicAddress);

public record EvaluateElectionOpenReadinessRequest(
    ElectionId ElectionId,
    IReadOnlyList<ElectionWarningCode>? RequiredWarningCodes = null);

public record StartElectionGovernedProposalRequest(
    ElectionId ElectionId,
    ElectionGovernedActionType ActionType,
    string ActorPublicAddress);

public record ApproveElectionGovernedProposalRequest(
    ElectionId ElectionId,
    Guid ProposalId,
    string ActorPublicAddress,
    string? ApprovalNote = null);

public record RetryElectionGovernedProposalExecutionRequest(
    ElectionId ElectionId,
    Guid ProposalId,
    string ActorPublicAddress);

public record OpenElectionRequest(
    ElectionId ElectionId,
    string ActorPublicAddress,
    IReadOnlyList<ElectionWarningCode>? RequiredWarningCodes = null,
    byte[]? FrozenEligibleVoterSetHash = null,
    string? TrusteePolicyExecutionReference = null,
    string? ReportingPolicyExecutionReference = null,
    string? ReviewWindowExecutionReference = null);

public record CloseElectionRequest(
    ElectionId ElectionId,
    string ActorPublicAddress,
    byte[]? AcceptedBallotSetHash = null,
    byte[]? FinalEncryptedTallyHash = null);

public record FinalizeElectionRequest(
    ElectionId ElectionId,
    string ActorPublicAddress,
    byte[]? AcceptedBallotSetHash = null,
    byte[]? FinalEncryptedTallyHash = null);

public enum ElectionCommandErrorCode
{
    None = 0,
    NotFound = 1,
    Forbidden = 2,
    InvalidState = 3,
    ValidationFailed = 4,
    DependencyBlocked = 5,
    Conflict = 6,
    NotSupported = 7,
}

public record ElectionCommandResult
{
    public bool IsSuccess { get; init; }
    public ElectionCommandErrorCode ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
    public ElectionRecord? Election { get; init; }
    public ElectionDraftSnapshotRecord? DraftSnapshot { get; init; }
    public ElectionBoundaryArtifactRecord? BoundaryArtifact { get; init; }
    public ElectionTrusteeInvitationRecord? TrusteeInvitation { get; init; }
    public ElectionGovernedProposalRecord? GovernedProposal { get; init; }
    public ElectionGovernedProposalApprovalRecord? GovernedProposalApproval { get; init; }

    public static ElectionCommandResult Success(
        ElectionRecord election,
        ElectionDraftSnapshotRecord? draftSnapshot = null,
        ElectionBoundaryArtifactRecord? boundaryArtifact = null,
        ElectionTrusteeInvitationRecord? trusteeInvitation = null,
        ElectionGovernedProposalRecord? governedProposal = null,
        ElectionGovernedProposalApprovalRecord? governedProposalApproval = null) =>
        new()
        {
            IsSuccess = true,
            ErrorCode = ElectionCommandErrorCode.None,
            Election = election,
            DraftSnapshot = draftSnapshot,
            BoundaryArtifact = boundaryArtifact,
            TrusteeInvitation = trusteeInvitation,
            GovernedProposal = governedProposal,
            GovernedProposalApproval = governedProposalApproval,
        };

    public static ElectionCommandResult Failure(
        ElectionCommandErrorCode errorCode,
        string errorMessage,
        IReadOnlyList<string>? validationErrors = null) =>
        new()
        {
            IsSuccess = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            ValidationErrors = validationErrors ?? Array.Empty<string>(),
        };
}

public record ElectionOpenValidationResult
{
    public bool IsReadyToOpen { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ElectionWarningCode> RequiredWarningCodes { get; init; } = Array.Empty<ElectionWarningCode>();
    public IReadOnlyList<ElectionWarningCode> MissingWarningAcknowledgements { get; init; } = Array.Empty<ElectionWarningCode>();

    public static ElectionOpenValidationResult Ready(IReadOnlyList<ElectionWarningCode> requiredWarningCodes) =>
        new()
        {
            IsReadyToOpen = true,
            RequiredWarningCodes = requiredWarningCodes,
        };

    public static ElectionOpenValidationResult NotReady(
        IReadOnlyList<string> validationErrors,
        IReadOnlyList<ElectionWarningCode> requiredWarningCodes,
        IReadOnlyList<ElectionWarningCode> missingWarningAcknowledgements) =>
        new()
        {
            IsReadyToOpen = false,
            ValidationErrors = validationErrors,
            RequiredWarningCodes = requiredWarningCodes,
            MissingWarningAcknowledgements = missingWarningAcknowledgements,
        };
}
