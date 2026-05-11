using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;

namespace HushNode.Elections;

public interface IElectionLifecycleService
{
    Task<ElectionCommandResult> CreateDraftAsync(CreateElectionDraftRequest request);

    Task<ElectionCommandResult> UpdateDraftAsync(UpdateElectionDraftRequest request);

    Task<ElectionCommandResult> RefreshProtocolPackageBindingAsync(RefreshElectionProtocolPackageBindingRequest request);

    Task<ElectionCommandResult> ImportRosterAsync(ImportElectionRosterRequest request);

    Task<ElectionCommandResult> ClaimRosterEntryAsync(ClaimElectionRosterEntryRequest request);

    Task<ElectionCommandResult> ActivateRosterEntryAsync(ActivateElectionRosterEntryRequest request);

    Task<ElectionCommandResult> InviteTrusteeAsync(InviteElectionTrusteeRequest request);

    Task<ElectionCommandResult> CreateReportAccessGrantAsync(CreateElectionReportAccessGrantRequest request);

    Task<ElectionCommandResult> AcceptTrusteeInvitationAsync(ResolveElectionTrusteeInvitationRequest request);

    Task<ElectionCommandResult> RejectTrusteeInvitationAsync(ResolveElectionTrusteeInvitationRequest request);

    Task<ElectionCommandResult> RevokeTrusteeInvitationAsync(ResolveElectionTrusteeInvitationRequest request);

    Task<ElectionCommandResult> StartElectionCeremonyAsync(StartElectionCeremonyRequest request);

    Task<ElectionCommandResult> RestartElectionCeremonyAsync(RestartElectionCeremonyRequest request);

    Task<ElectionCommandResult> PublishElectionCeremonyTransportKeyAsync(PublishElectionCeremonyTransportKeyRequest request);

    Task<ElectionCommandResult> JoinElectionCeremonyAsync(JoinElectionCeremonyRequest request);

    Task<ElectionCommandResult> RecordElectionCeremonySelfTestSuccessAsync(RecordElectionCeremonySelfTestRequest request);

    Task<ElectionCommandResult> SubmitElectionCeremonyMaterialAsync(SubmitElectionCeremonyMaterialRequest request);

    Task<ElectionCommandResult> RecordElectionCeremonyValidationFailureAsync(RecordElectionCeremonyValidationFailureRequest request);

    Task<ElectionCommandResult> CompleteElectionCeremonyTrusteeAsync(CompleteElectionCeremonyTrusteeRequest request);

    Task<ElectionCommandResult> RecordElectionCeremonyShareExportAsync(RecordElectionCeremonyShareExportRequest request);

    Task<ElectionCommandResult> RecordElectionCeremonyShareImportAsync(RecordElectionCeremonyShareImportRequest request);

    Task<ElectionOpenValidationResult> EvaluateOpenReadinessAsync(EvaluateElectionOpenReadinessRequest request);

    Task<ElectionCommandResult> StartGovernedProposalAsync(StartElectionGovernedProposalRequest request);

    Task<ElectionCommandResult> ApproveGovernedProposalAsync(ApproveElectionGovernedProposalRequest request);

    Task<ElectionCommandResult> RetryGovernedProposalExecutionAsync(RetryElectionGovernedProposalExecutionRequest request);

    Task<ElectionCommandResult> OpenElectionAsync(OpenElectionRequest request);

    Task<ElectionCommandResult> CloseElectionAsync(CloseElectionRequest request);

    Task<ElectionCommandResult> FinalizeElectionAsync(FinalizeElectionRequest request);

    Task<ElectionCommandResult> SubmitFinalizationShareAsync(SubmitElectionFinalizationShareRequest request);

    Task<ElectionCommandResult> ExecuteCloseCountingJobAsync(ExecuteElectionCloseCountingJobRequest request);

    Task<ElectionCommitmentRegistrationResult> RegisterVotingCommitmentAsync(RegisterElectionVotingCommitmentRequest request);

    Task<ElectionPreparedBallotCommitmentResult> RegisterPreparedBallotCommitmentAsync(
        RegisterPreparedBallotCommitmentRequest request);

    Task<ElectionSpoilPreparedBallotResult> SpoilPreparedBallotAsync(SpoilPreparedBallotRequest request);

    Task<ElectionCastAcceptanceResult> AcceptBallotCastAsync(AcceptElectionBallotCastRequest request);
}

public record CreateElectionDraftRequest(
    string OwnerPublicAddress,
    string ActorPublicAddress,
    string SnapshotReason,
    ElectionDraftSpecification Draft,
    ElectionId? PreassignedElectionId = null,
    Guid? SourceTransactionId = null,
    long? SourceBlockHeight = null,
    Guid? SourceBlockId = null);

public record UpdateElectionDraftRequest(
    ElectionId ElectionId,
    string ActorPublicAddress,
    string SnapshotReason,
    ElectionDraftSpecification Draft,
    Guid? SourceTransactionId = null,
    long? SourceBlockHeight = null,
    Guid? SourceBlockId = null);

public record RefreshElectionProtocolPackageBindingRequest(
    ElectionId ElectionId,
    string ActorPublicAddress,
    Guid? SourceTransactionId = null,
    long? SourceBlockHeight = null,
    Guid? SourceBlockId = null);

public record ImportElectionRosterRequest(
    ElectionId ElectionId,
    string ActorPublicAddress,
    IReadOnlyList<ElectionRosterImportItem> RosterEntries,
    Guid? SourceTransactionId = null,
    long? SourceBlockHeight = null,
    Guid? SourceBlockId = null);

public record ClaimElectionRosterEntryRequest(
    ElectionId ElectionId,
    string ActorPublicAddress,
    string OrganizationVoterId,
    string VerificationCode,
    Guid? SourceTransactionId = null,
    long? SourceBlockHeight = null,
    Guid? SourceBlockId = null);

public record ActivateElectionRosterEntryRequest(
    ElectionId ElectionId,
    string ActorPublicAddress,
    string OrganizationVoterId,
    Guid? SourceTransactionId = null,
    long? SourceBlockHeight = null,
    Guid? SourceBlockId = null);

public record InviteElectionTrusteeRequest(
    ElectionId ElectionId,
    string ActorPublicAddress,
    string TrusteeUserAddress,
    string? TrusteeDisplayName,
    Guid? PreassignedInvitationId = null,
    Guid? SourceTransactionId = null,
    long? SourceBlockHeight = null,
    Guid? SourceBlockId = null);

public record CreateElectionReportAccessGrantRequest(
    ElectionId ElectionId,
    string ActorPublicAddress,
    string DesignatedAuditorPublicAddress,
    Guid? SourceTransactionId = null,
    long? SourceBlockHeight = null,
    Guid? SourceBlockId = null);

public record ResolveElectionTrusteeInvitationRequest(
    ElectionId ElectionId,
    Guid InvitationId,
    string ActorPublicAddress,
    Guid? SourceTransactionId = null,
    long? SourceBlockHeight = null,
    Guid? SourceBlockId = null);

public record StartElectionCeremonyRequest(
    ElectionId ElectionId,
    string ActorPublicAddress,
    string ProfileId);

public record RestartElectionCeremonyRequest(
    ElectionId ElectionId,
    string ActorPublicAddress,
    string ProfileId,
    string RestartReason);

public record PublishElectionCeremonyTransportKeyRequest(
    ElectionId ElectionId,
    Guid CeremonyVersionId,
    string ActorPublicAddress,
    string TransportPublicKeyFingerprint);

public record JoinElectionCeremonyRequest(
    ElectionId ElectionId,
    Guid CeremonyVersionId,
    string ActorPublicAddress);

public record RecordElectionCeremonySelfTestRequest(
    ElectionId ElectionId,
    Guid CeremonyVersionId,
    string ActorPublicAddress);

public record SubmitElectionCeremonyMaterialRequest(
    ElectionId ElectionId,
    Guid CeremonyVersionId,
    string ActorPublicAddress,
    string? RecipientTrusteeUserAddress,
    string MessageType,
    string PayloadVersion,
    byte[] EncryptedPayload,
    string PayloadFingerprint,
    string ShareVersion,
    byte[] CloseCountingPublicCommitment);

public record RecordElectionCeremonyValidationFailureRequest(
    ElectionId ElectionId,
    Guid CeremonyVersionId,
    string ActorPublicAddress,
    string TrusteeUserAddress,
    string ValidationFailureReason,
    string? EvidenceReference = null);

public record CompleteElectionCeremonyTrusteeRequest(
    ElectionId ElectionId,
    Guid CeremonyVersionId,
    string ActorPublicAddress,
    string TrusteeUserAddress,
    string ShareVersion,
    string? TallyPublicKeyFingerprint = null);

public record RecordElectionCeremonyShareExportRequest(
    ElectionId ElectionId,
    Guid CeremonyVersionId,
    string ActorPublicAddress,
    string ShareVersion);

public record RecordElectionCeremonyShareImportRequest(
    ElectionId ElectionId,
    Guid CeremonyVersionId,
    string ActorPublicAddress,
    ElectionId ImportedElectionId,
    Guid ImportedCeremonyVersionId,
    string ImportedTrusteeUserAddress,
    string ImportedShareVersion);

public record EvaluateElectionOpenReadinessRequest(
    ElectionId ElectionId,
    IReadOnlyList<ElectionWarningCode>? RequiredWarningCodes = null);

public record StartElectionGovernedProposalRequest(
    ElectionId ElectionId,
    ElectionGovernedActionType ActionType,
    string ActorPublicAddress,
    Guid? PreassignedProposalId = null,
    Guid? SourceTransactionId = null,
    long? SourceBlockHeight = null,
    Guid? SourceBlockId = null);

public record ApproveElectionGovernedProposalRequest(
    ElectionId ElectionId,
    Guid ProposalId,
    string ActorPublicAddress,
    string? ApprovalNote = null,
    Guid? SourceTransactionId = null,
    long? SourceBlockHeight = null,
    Guid? SourceBlockId = null);

public record RetryElectionGovernedProposalExecutionRequest(
    ElectionId ElectionId,
    Guid ProposalId,
    string ActorPublicAddress,
    Guid? SourceTransactionId = null,
    long? SourceBlockHeight = null,
    Guid? SourceBlockId = null);

public record OpenElectionRequest(
    ElectionId ElectionId,
    string ActorPublicAddress,
    IReadOnlyList<ElectionWarningCode>? RequiredWarningCodes = null,
    byte[]? FrozenEligibleVoterSetHash = null,
    string? TrusteePolicyExecutionReference = null,
    string? ReportingPolicyExecutionReference = null,
    string? ReviewWindowExecutionReference = null,
    Guid? SourceTransactionId = null,
    long? SourceBlockHeight = null,
    Guid? SourceBlockId = null);

public record CloseElectionRequest(
    ElectionId ElectionId,
    string ActorPublicAddress,
    byte[]? AcceptedBallotSetHash = null,
    byte[]? FinalEncryptedTallyHash = null,
    Guid? SourceTransactionId = null,
    long? SourceBlockHeight = null,
    Guid? SourceBlockId = null);

public record FinalizeElectionRequest(
    ElectionId ElectionId,
    string ActorPublicAddress,
    byte[]? AcceptedBallotSetHash = null,
    byte[]? FinalEncryptedTallyHash = null,
    Guid? SourceTransactionId = null,
    long? SourceBlockHeight = null,
    Guid? SourceBlockId = null);

public record SubmitElectionFinalizationShareRequest(
    ElectionId ElectionId,
    Guid FinalizationSessionId,
    string ActorPublicAddress,
    int ShareIndex,
    string ShareVersion,
    ElectionFinalizationTargetType TargetType,
    Guid ClaimedCloseArtifactId,
    byte[]? ClaimedAcceptedBallotSetHash,
    byte[]? ClaimedFinalEncryptedTallyHash,
    string ClaimedTargetTallyId,
    Guid? ClaimedCeremonyVersionId,
    string? ClaimedTallyPublicKeyFingerprint,
    string? ShareMaterial = null,
    Guid? CloseCountingJobId = null,
    string? ExecutorKeyAlgorithm = null,
    string? EncryptedExecutorSubmission = null,
    Guid? SourceTransactionId = null,
    long? SourceBlockHeight = null,
    Guid? SourceBlockId = null);

public record ExecuteElectionCloseCountingJobRequest(
    Guid CloseCountingJobId,
    string LeaseHolderId,
    Guid? SourceTransactionId = null,
    long? SourceBlockHeight = null,
    Guid? SourceBlockId = null);

public record RegisterElectionVotingCommitmentRequest(
    ElectionId ElectionId,
    string ActorPublicAddress,
    string CommitmentHash,
    string? OrganizationVoterId = null);

public record RegisterPreparedBallotCommitmentRequest(
    ElectionId ElectionId,
    string ActorPublicAddress,
    Guid PreparedBallotId,
    string PreparedBallotHash,
    int BallotDefinitionVersion,
    byte[] BallotDefinitionHash,
    string CeremonyProfileId,
    string ProofStatementId,
    DateTime? PrecommittedAt = null,
    Guid? SourceTransactionId = null,
    long? SourceBlockHeight = null,
    Guid? SourceBlockId = null,
    string? OrganizationVoterId = null);

public record SpoilPreparedBallotRequest(
    ElectionId ElectionId,
    string ActorPublicAddress,
    Guid PreparedBallotId,
    string PreparedBallotHash,
    string SpoiledTranscriptHash,
    string SpoilRecordHash,
    string LocalVerifierVersion,
    DateTime? SpoiledAt = null,
    Guid? SourceTransactionId = null,
    long? SourceBlockHeight = null,
    Guid? SourceBlockId = null,
    string? OrganizationVoterId = null);

public record AcceptElectionBallotCastRequest(
    ElectionId ElectionId,
    string ActorPublicAddress,
    string IdempotencyKey,
    string EncryptedBallotPackage,
    string ProofBundle,
    string BallotNullifier,
    Guid OpenArtifactId,
    byte[] EligibleSetHash,
    Guid CeremonyVersionId,
    string DkgProfileId,
    string TallyPublicKeyFingerprint,
    Guid? PreparedBallotId = null,
    string? PreparedBallotHash = null,
    string? ReceiptCommitment = null,
    string? ReceiptCommitmentScheme = null,
    int? BallotDefinitionVersion = null,
    byte[]? BallotDefinitionHash = null,
    string? OrganizationVoterId = null);

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

public enum ElectionCommitmentRegistrationFailureReason
{
    None = 0,
    ValidationFailed = 1,
    NotFound = 2,
    NotLinked = 3,
    NotActive = 4,
    AlreadyRegistered = 5,
    ElectionNotOpenableForRegistration = 6,
    ClosePersisted = 7,
}

public enum ElectionCastAcceptanceFailureReason
{
    None = 0,
    ValidationFailed = 1,
    NotFound = 2,
    NotLinked = 3,
    NotActive = 4,
    CommitmentMissing = 5,
    StillProcessing = 6,
    AlreadyUsed = 7,
    DuplicateNullifier = 8,
    WrongElectionContext = 9,
    ClosePersisted = 10,
    AlreadyVoted = 11,
    PreparedBallotMissing = 12,
    PreparedBallotHashMismatch = 13,
    PreparedBallotExpired = 14,
    PreparedBallotAlreadySpoiled = 15,
    PreparedBallotAlreadyCast = 16,
    ChallengeRequiredBeforeCast = 17,
    BallotDefinitionHashMismatch = 18,
    ReceiptCommitmentMissing = 19,
    UnsupportedCeremonyProfile = 20,
}

public enum ElectionPreparedBallotCommitmentFailureReason
{
    None = 0,
    ValidationFailed = 1,
    NotFound = 2,
    NotLinked = 3,
    NotActive = 4,
    CommitmentMissing = 5,
    ElectionNotOpen = 6,
    BallotDefinitionHashMismatch = 7,
    UnsupportedCeremonyProfile = 8,
    DuplicatePreparedBallot = 9,
    ClosePersisted = 10,
}

public enum ElectionSpoilPreparedBallotFailureReason
{
    None = 0,
    ValidationFailed = 1,
    NotFound = 2,
    NotLinked = 3,
    NotActive = 4,
    PreparedBallotMissing = 5,
    PreparedBallotHashMismatch = 6,
    PreparedBallotExpired = 7,
    PreparedBallotAlreadySpoiled = 8,
    PreparedBallotAlreadyCast = 9,
    ElectionNotOpen = 10,
    ClosePersisted = 11,
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
    public ElectionRosterEntryRecord? RosterEntry { get; init; }
    public IReadOnlyList<ElectionRosterEntryRecord> RosterEntries { get; init; } = Array.Empty<ElectionRosterEntryRecord>();
    public ElectionRosterImportEvidenceRecord? RosterImportEvidence { get; init; }
    public ElectionEligibilityActivationEventRecord? EligibilityActivationEvent { get; init; }
    public ElectionParticipationRecord? ParticipationRecord { get; init; }
    public ElectionEligibilitySnapshotRecord? EligibilitySnapshot { get; init; }
    public ElectionTrusteeInvitationRecord? TrusteeInvitation { get; init; }
    public ElectionReportAccessGrantRecord? ReportAccessGrant { get; init; }
    public ElectionGovernedProposalRecord? GovernedProposal { get; init; }
    public ElectionGovernedProposalApprovalRecord? GovernedProposalApproval { get; init; }
    public ElectionCeremonyProfileRecord? CeremonyProfile { get; init; }
    public ElectionCeremonyVersionRecord? CeremonyVersion { get; init; }
    public IReadOnlyList<ElectionCeremonyTranscriptEventRecord> CeremonyTranscriptEvents { get; init; } = Array.Empty<ElectionCeremonyTranscriptEventRecord>();
    public ElectionCeremonyTrusteeStateRecord? CeremonyTrusteeState { get; init; }
    public ElectionCeremonyMessageEnvelopeRecord? CeremonyMessageEnvelope { get; init; }
    public ElectionCeremonyShareCustodyRecord? CeremonyShareCustody { get; init; }
    public ElectionFinalizationSessionRecord? FinalizationSession { get; init; }
    public ElectionFinalizationShareRecord? FinalizationShare { get; init; }
    public ElectionFinalizationReleaseEvidenceRecord? FinalizationReleaseEvidence { get; init; }
    public ProtocolPackageBindingRecord? ProtocolPackageBinding { get; init; }

    public static ElectionCommandResult Success(
        ElectionRecord election,
        ElectionDraftSnapshotRecord? draftSnapshot = null,
        ElectionBoundaryArtifactRecord? boundaryArtifact = null,
        ElectionRosterEntryRecord? rosterEntry = null,
        IReadOnlyList<ElectionRosterEntryRecord>? rosterEntries = null,
        ElectionRosterImportEvidenceRecord? rosterImportEvidence = null,
        ElectionEligibilityActivationEventRecord? eligibilityActivationEvent = null,
        ElectionParticipationRecord? participationRecord = null,
        ElectionEligibilitySnapshotRecord? eligibilitySnapshot = null,
        ElectionTrusteeInvitationRecord? trusteeInvitation = null,
        ElectionReportAccessGrantRecord? reportAccessGrant = null,
        ElectionGovernedProposalRecord? governedProposal = null,
        ElectionGovernedProposalApprovalRecord? governedProposalApproval = null,
        ElectionCeremonyProfileRecord? ceremonyProfile = null,
        ElectionCeremonyVersionRecord? ceremonyVersion = null,
        IReadOnlyList<ElectionCeremonyTranscriptEventRecord>? ceremonyTranscriptEvents = null,
        ElectionCeremonyTrusteeStateRecord? ceremonyTrusteeState = null,
        ElectionCeremonyMessageEnvelopeRecord? ceremonyMessageEnvelope = null,
        ElectionCeremonyShareCustodyRecord? ceremonyShareCustody = null,
        ElectionFinalizationSessionRecord? finalizationSession = null,
        ElectionFinalizationShareRecord? finalizationShare = null,
        ElectionFinalizationReleaseEvidenceRecord? finalizationReleaseEvidence = null,
        ProtocolPackageBindingRecord? protocolPackageBinding = null) =>
        new()
        {
            IsSuccess = true,
            ErrorCode = ElectionCommandErrorCode.None,
            Election = election,
            DraftSnapshot = draftSnapshot,
            BoundaryArtifact = boundaryArtifact,
            RosterEntry = rosterEntry,
            RosterEntries = rosterEntries ?? Array.Empty<ElectionRosterEntryRecord>(),
            RosterImportEvidence = rosterImportEvidence,
            EligibilityActivationEvent = eligibilityActivationEvent,
            ParticipationRecord = participationRecord,
            EligibilitySnapshot = eligibilitySnapshot,
            TrusteeInvitation = trusteeInvitation,
            ReportAccessGrant = reportAccessGrant,
            GovernedProposal = governedProposal,
            GovernedProposalApproval = governedProposalApproval,
            CeremonyProfile = ceremonyProfile,
            CeremonyVersion = ceremonyVersion,
            CeremonyTranscriptEvents = ceremonyTranscriptEvents ?? Array.Empty<ElectionCeremonyTranscriptEventRecord>(),
            CeremonyTrusteeState = ceremonyTrusteeState,
            CeremonyMessageEnvelope = ceremonyMessageEnvelope,
            CeremonyShareCustody = ceremonyShareCustody,
            FinalizationSession = finalizationSession,
            FinalizationShare = finalizationShare,
            FinalizationReleaseEvidence = finalizationReleaseEvidence,
            ProtocolPackageBinding = protocolPackageBinding,
        };

    public static ElectionCommandResult Failure(
        ElectionCommandErrorCode errorCode,
        string errorMessage,
        IReadOnlyList<string>? validationErrors = null,
        ElectionRosterImportEvidenceRecord? rosterImportEvidence = null) =>
        new()
        {
            IsSuccess = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            ValidationErrors = validationErrors ?? Array.Empty<string>(),
            RosterImportEvidence = rosterImportEvidence,
        };
}

public record ElectionCommitmentRegistrationResult
{
    public bool IsSuccess { get; init; }
    public ElectionCommitmentRegistrationFailureReason FailureReason { get; init; }
    public string? ErrorMessage { get; init; }
    public ElectionRecord? Election { get; init; }
    public ElectionRosterEntryRecord? RosterEntry { get; init; }
    public ElectionCommitmentRegistrationRecord? CommitmentRegistration { get; init; }

    public static ElectionCommitmentRegistrationResult Success(
        ElectionRecord election,
        ElectionRosterEntryRecord rosterEntry,
        ElectionCommitmentRegistrationRecord commitmentRegistration) =>
        new()
        {
            IsSuccess = true,
            FailureReason = ElectionCommitmentRegistrationFailureReason.None,
            Election = election,
            RosterEntry = rosterEntry,
            CommitmentRegistration = commitmentRegistration,
        };

    public static ElectionCommitmentRegistrationResult Failure(
        ElectionCommitmentRegistrationFailureReason failureReason,
        string errorMessage) =>
        new()
        {
            IsSuccess = false,
            FailureReason = failureReason,
            ErrorMessage = errorMessage,
        };
}

public record ElectionCastAcceptanceResult
{
    public bool IsSuccess { get; init; }
    public ElectionCastAcceptanceFailureReason FailureReason { get; init; }
    public string? ErrorMessage { get; init; }
    public ElectionRecord? Election { get; init; }
    public ElectionRosterEntryRecord? RosterEntry { get; init; }
    public ElectionCommitmentRegistrationRecord? CommitmentRegistration { get; init; }
    public ElectionParticipationRecord? ParticipationRecord { get; init; }
    public ElectionCheckoffConsumptionRecord? CheckoffConsumption { get; init; }
    public ElectionAcceptedBallotRecord? AcceptedBallot { get; init; }
    public ElectionCastIdempotencyRecord? CastIdempotencyRecord { get; init; }
    public ElectionPreparedBallotCommitmentRecord? PreparedBallotCommitment { get; init; }
    public ElectionBoundReceiptRecord? BoundReceipt { get; init; }

    public static ElectionCastAcceptanceResult Success(
        ElectionRecord election,
        ElectionRosterEntryRecord rosterEntry,
        ElectionCommitmentRegistrationRecord commitmentRegistration,
        ElectionParticipationRecord participationRecord,
        ElectionCheckoffConsumptionRecord checkoffConsumption,
        ElectionAcceptedBallotRecord acceptedBallot,
        ElectionCastIdempotencyRecord castIdempotencyRecord,
        ElectionPreparedBallotCommitmentRecord? preparedBallotCommitment = null,
        ElectionBoundReceiptRecord? boundReceipt = null) =>
        new()
        {
            IsSuccess = true,
            FailureReason = ElectionCastAcceptanceFailureReason.None,
            Election = election,
            RosterEntry = rosterEntry,
            CommitmentRegistration = commitmentRegistration,
            ParticipationRecord = participationRecord,
            CheckoffConsumption = checkoffConsumption,
            AcceptedBallot = acceptedBallot,
            CastIdempotencyRecord = castIdempotencyRecord,
            PreparedBallotCommitment = preparedBallotCommitment,
            BoundReceipt = boundReceipt,
        };

    public static ElectionCastAcceptanceResult Failure(
        ElectionCastAcceptanceFailureReason failureReason,
        string errorMessage) =>
        new()
        {
            IsSuccess = false,
            FailureReason = failureReason,
            ErrorMessage = errorMessage,
        };
}

public record ElectionPreparedBallotCommitmentResult
{
    public bool IsSuccess { get; init; }
    public ElectionPreparedBallotCommitmentFailureReason FailureReason { get; init; }
    public string? ErrorMessage { get; init; }
    public ElectionRecord? Election { get; init; }
    public ElectionRosterEntryRecord? RosterEntry { get; init; }
    public ElectionCommitmentRegistrationRecord? CommitmentRegistration { get; init; }
    public ElectionVoterCeremonyRecord? CeremonyRecord { get; init; }
    public ElectionPreparedBallotCommitmentRecord? PreparedBallotCommitment { get; init; }

    public static ElectionPreparedBallotCommitmentResult Success(
        ElectionRecord election,
        ElectionRosterEntryRecord rosterEntry,
        ElectionCommitmentRegistrationRecord commitmentRegistration,
        ElectionVoterCeremonyRecord ceremonyRecord,
        ElectionPreparedBallotCommitmentRecord preparedBallotCommitment) =>
        new()
        {
            IsSuccess = true,
            FailureReason = ElectionPreparedBallotCommitmentFailureReason.None,
            Election = election,
            RosterEntry = rosterEntry,
            CommitmentRegistration = commitmentRegistration,
            CeremonyRecord = ceremonyRecord,
            PreparedBallotCommitment = preparedBallotCommitment,
        };

    public static ElectionPreparedBallotCommitmentResult Failure(
        ElectionPreparedBallotCommitmentFailureReason failureReason,
        string errorMessage) =>
        new()
        {
            IsSuccess = false,
            FailureReason = failureReason,
            ErrorMessage = errorMessage,
        };
}

public record ElectionSpoilPreparedBallotResult
{
    public bool IsSuccess { get; init; }
    public ElectionSpoilPreparedBallotFailureReason FailureReason { get; init; }
    public string? ErrorMessage { get; init; }
    public ElectionRecord? Election { get; init; }
    public ElectionRosterEntryRecord? RosterEntry { get; init; }
    public ElectionPreparedBallotCommitmentRecord? PreparedBallotCommitment { get; init; }
    public ElectionSpoiledPreparedBallotRecord? SpoiledPreparedBallot { get; init; }
    public ElectionVoterCeremonyRecord? CeremonyRecord { get; init; }

    public static ElectionSpoilPreparedBallotResult Success(
        ElectionRecord election,
        ElectionRosterEntryRecord rosterEntry,
        ElectionPreparedBallotCommitmentRecord preparedBallotCommitment,
        ElectionSpoiledPreparedBallotRecord spoiledPreparedBallot,
        ElectionVoterCeremonyRecord ceremonyRecord) =>
        new()
        {
            IsSuccess = true,
            FailureReason = ElectionSpoilPreparedBallotFailureReason.None,
            Election = election,
            RosterEntry = rosterEntry,
            PreparedBallotCommitment = preparedBallotCommitment,
            SpoiledPreparedBallot = spoiledPreparedBallot,
            CeremonyRecord = ceremonyRecord,
        };

    public static ElectionSpoilPreparedBallotResult Failure(
        ElectionSpoilPreparedBallotFailureReason failureReason,
        string errorMessage) =>
        new()
        {
            IsSuccess = false,
            FailureReason = failureReason,
            ErrorMessage = errorMessage,
        };
}

public record ElectionOpenValidationResult
{
    public bool IsReadyToOpen { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ElectionWarningCode> RequiredWarningCodes { get; init; } = Array.Empty<ElectionWarningCode>();
    public IReadOnlyList<ElectionWarningCode> MissingWarningAcknowledgements { get; init; } = Array.Empty<ElectionWarningCode>();
    public ElectionCeremonyBindingSnapshot? CeremonySnapshot { get; init; }
    public ProtocolPackageBindingStatus ProtocolPackageBindingStatus { get; init; } = ProtocolPackageBindingStatus.Missing;
    public string ProtocolPackageBindingMessage { get; init; } = string.Empty;
    public ProtocolPackageBindingRecord? ProtocolPackageBinding { get; init; }
    public ElectionTrusteeControlDomainSummaryRecord? Sp06Summary { get; init; }
    public ElectionSp07OpenReadinessSummary? Sp07Summary { get; init; }
    public ElectionSp08OpenReadinessSummary? Sp08Summary { get; init; }

    public static ElectionOpenValidationResult Ready(
        IReadOnlyList<ElectionWarningCode> requiredWarningCodes,
        ElectionCeremonyBindingSnapshot? ceremonySnapshot = null,
        ProtocolPackageBindingOpenValidation? protocolPackageValidation = null,
        ElectionTrusteeControlDomainSummaryRecord? sp06Summary = null,
        ElectionSp07OpenReadinessSummary? sp07Summary = null,
        ElectionSp08OpenReadinessSummary? sp08Summary = null) =>
        new()
        {
            IsReadyToOpen = true,
            RequiredWarningCodes = requiredWarningCodes,
            CeremonySnapshot = ceremonySnapshot,
            ProtocolPackageBindingStatus = protocolPackageValidation?.Status ?? ProtocolPackageBindingStatus.Missing,
            ProtocolPackageBindingMessage = protocolPackageValidation?.ErrorMessage ?? string.Empty,
            ProtocolPackageBinding = protocolPackageValidation?.Binding,
            Sp06Summary = sp06Summary,
            Sp07Summary = sp07Summary,
            Sp08Summary = sp08Summary,
        };

    public static ElectionOpenValidationResult NotReady(
        IReadOnlyList<string> validationErrors,
        IReadOnlyList<ElectionWarningCode> requiredWarningCodes,
        IReadOnlyList<ElectionWarningCode> missingWarningAcknowledgements,
        ElectionCeremonyBindingSnapshot? ceremonySnapshot = null,
        ProtocolPackageBindingOpenValidation? protocolPackageValidation = null,
        ElectionTrusteeControlDomainSummaryRecord? sp06Summary = null,
        ElectionSp07OpenReadinessSummary? sp07Summary = null,
        ElectionSp08OpenReadinessSummary? sp08Summary = null) =>
        new()
        {
            IsReadyToOpen = false,
            ValidationErrors = validationErrors,
            RequiredWarningCodes = requiredWarningCodes,
            MissingWarningAcknowledgements = missingWarningAcknowledgements,
            CeremonySnapshot = ceremonySnapshot,
            ProtocolPackageBindingStatus = protocolPackageValidation?.Status ?? ProtocolPackageBindingStatus.Missing,
            ProtocolPackageBindingMessage = protocolPackageValidation?.ErrorMessage ?? string.Empty,
            ProtocolPackageBinding = protocolPackageValidation?.Binding,
            Sp06Summary = sp06Summary,
            Sp07Summary = sp07Summary,
            Sp08Summary = sp08Summary,
        };
}

public sealed record ElectionSp07OpenReadinessSummary(
    bool EvidenceExpected,
    string PublicationProofMode,
    string ProofConstruction,
    string StatementId,
    string ExternalReviewStatus,
    int IntendedAcceptedBallotCount,
    int CiphertextSlotCount,
    int PlannedChunkCount,
    IReadOnlyList<ElectionSp07OpenReadinessBlocker> ReadinessBlockers)
{
    public bool IsReadyForOpen => ReadinessBlockers.All(x => !x.BlocksOpen);
}

public sealed record ElectionSp07OpenReadinessBlocker(
    string Code,
    string Message,
    bool BlocksOpen,
    bool BlocksFinalization);

public sealed record ElectionSp08OpenReadinessSummary(
    bool EvidenceExpected,
    string EvidenceMode,
    bool NotForReleaseIntegrityClaims,
    bool BlocksHighAssurance,
    string ReleaseManifestName,
    string ReleaseManifestHash,
    string ProtocolPackageManifestName,
    string ProtocolPackageManifestHash,
    string PrimaryResultCode,
    string PrimaryIssue,
    int ComponentCount,
    int LifecycleBindingCount,
    int EvidenceFileCount,
    bool MobileEvidenceIncluded,
    IReadOnlyList<ElectionSp08OpenReadinessBlocker> ReadinessBlockers)
{
    public bool PublicEvidenceAvailable { get; init; }
    public bool RestrictedEvidenceAvailable { get; init; }
    public IReadOnlyList<ElectionSp08ReleaseComponentArtifactRecord> Components { get; init; } =
        Array.Empty<ElectionSp08ReleaseComponentArtifactRecord>();
    public IReadOnlyList<ElectionSp08LifecycleReleaseBindingRecord> LifecycleBindings { get; init; } =
        Array.Empty<ElectionSp08LifecycleReleaseBindingRecord>();

    public bool IsReadyForOpen => !BlocksHighAssurance && ReadinessBlockers.All(x => !x.BlocksOpen);
}

public sealed record ElectionSp08OpenReadinessBlocker(
    string Code,
    string Message,
    bool BlocksOpen,
    bool BlocksFinalization);
