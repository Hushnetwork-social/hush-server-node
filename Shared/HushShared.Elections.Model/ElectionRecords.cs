namespace HushShared.Elections.Model;

public record ElectionRecord(
    ElectionId ElectionId,
    string Title,
    string? ShortDescription,
    string OwnerPublicAddress,
    string? ExternalReferenceCode,
    ElectionLifecycleState LifecycleState,
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
    int CurrentDraftRevision,
    IReadOnlyList<ElectionOptionDefinition> Options,
    IReadOnlyList<ElectionWarningCode> AcknowledgedWarningCodes,
    int? RequiredApprovalCount,
    DateTime CreatedAt,
    DateTime LastUpdatedAt,
    DateTime? OpenedAt,
    DateTime? ClosedAt,
    DateTime? FinalizedAt,
    DateTime? TallyReadyAt,
    Guid? OpenArtifactId,
    Guid? CloseArtifactId,
    Guid? FinalizeArtifactId);

public record ElectionDraftSnapshotRecord(
    Guid Id,
    ElectionId ElectionId,
    int DraftRevision,
    ElectionMetadataSnapshot Metadata,
    ElectionFrozenPolicySnapshot Policy,
    IReadOnlyList<ElectionOptionDefinition> Options,
    IReadOnlyList<ElectionWarningCode> AcknowledgedWarningCodes,
    string SnapshotReason,
    DateTime RecordedAt,
    string RecordedByPublicAddress,
    Guid? SourceTransactionId,
    long? SourceBlockHeight,
    Guid? SourceBlockId);

public record ElectionBoundaryArtifactRecord(
    Guid Id,
    ElectionId ElectionId,
    ElectionBoundaryArtifactType ArtifactType,
    ElectionLifecycleState LifecycleState,
    int SourceDraftRevision,
    ElectionMetadataSnapshot Metadata,
    ElectionFrozenPolicySnapshot Policy,
    IReadOnlyList<ElectionOptionDefinition> Options,
    IReadOnlyList<ElectionWarningCode> AcknowledgedWarningCodes,
    ElectionTrusteeBoundarySnapshot? TrusteeSnapshot,
    byte[]? FrozenEligibleVoterSetHash,
    string? TrusteePolicyExecutionReference,
    string? ReportingPolicyExecutionReference,
    string? ReviewWindowExecutionReference,
    byte[]? AcceptedBallotSetHash,
    byte[]? FinalEncryptedTallyHash,
    DateTime RecordedAt,
    string RecordedByPublicAddress,
    Guid? SourceTransactionId,
    long? SourceBlockHeight,
    Guid? SourceBlockId);

public record ElectionWarningAcknowledgementRecord(
    Guid Id,
    ElectionId ElectionId,
    ElectionWarningCode WarningCode,
    int DraftRevision,
    string AcknowledgedByPublicAddress,
    DateTime AcknowledgedAt,
    Guid? SourceTransactionId,
    long? SourceBlockHeight,
    Guid? SourceBlockId);

public record ElectionTrusteeInvitationRecord(
    Guid Id,
    ElectionId ElectionId,
    string TrusteeUserAddress,
    string? TrusteeDisplayName,
    string InvitedByPublicAddress,
    Guid? LinkedMessageId,
    ElectionTrusteeInvitationStatus Status,
    int SentAtDraftRevision,
    DateTime SentAt,
    int? ResolvedAtDraftRevision,
    DateTime? RespondedAt,
    DateTime? RevokedAt,
    Guid? LatestTransactionId,
    long? LatestBlockHeight,
    Guid? LatestBlockId)
{
    public ElectionTrusteeInvitationRecord Accept(
        DateTime respondedAt,
        int resolvedAtDraftRevision,
        ElectionLifecycleState lifecycleState,
        Guid? latestTransactionId = null,
        long? latestBlockHeight = null,
        Guid? latestBlockId = null)
    {
        EnsurePending();
        EnsureDraftOnly(lifecycleState);
        EnsureResolvedRevisionIsValid(resolvedAtDraftRevision);

        return this with
        {
            Status = ElectionTrusteeInvitationStatus.Accepted,
            ResolvedAtDraftRevision = resolvedAtDraftRevision,
            RespondedAt = respondedAt,
            RevokedAt = null,
            LatestTransactionId = latestTransactionId,
            LatestBlockHeight = latestBlockHeight,
            LatestBlockId = latestBlockId,
        };
    }

    public ElectionTrusteeInvitationRecord Reject(
        DateTime respondedAt,
        int resolvedAtDraftRevision,
        ElectionLifecycleState lifecycleState,
        Guid? latestTransactionId = null,
        long? latestBlockHeight = null,
        Guid? latestBlockId = null)
    {
        EnsurePending();
        EnsureDraftOnly(lifecycleState);
        EnsureResolvedRevisionIsValid(resolvedAtDraftRevision);

        return this with
        {
            Status = ElectionTrusteeInvitationStatus.Rejected,
            ResolvedAtDraftRevision = resolvedAtDraftRevision,
            RespondedAt = respondedAt,
            RevokedAt = null,
            LatestTransactionId = latestTransactionId,
            LatestBlockHeight = latestBlockHeight,
            LatestBlockId = latestBlockId,
        };
    }

    public ElectionTrusteeInvitationRecord Revoke(
        DateTime revokedAt,
        int resolvedAtDraftRevision,
        ElectionLifecycleState lifecycleState,
        Guid? latestTransactionId = null,
        long? latestBlockHeight = null,
        Guid? latestBlockId = null)
    {
        EnsurePending();
        EnsureDraftOnly(lifecycleState);
        EnsureResolvedRevisionIsValid(resolvedAtDraftRevision);

        return this with
        {
            Status = ElectionTrusteeInvitationStatus.Revoked,
            ResolvedAtDraftRevision = resolvedAtDraftRevision,
            RespondedAt = null,
            RevokedAt = revokedAt,
            LatestTransactionId = latestTransactionId,
            LatestBlockHeight = latestBlockHeight,
            LatestBlockId = latestBlockId,
        };
    }

    private void EnsurePending()
    {
        if (Status != ElectionTrusteeInvitationStatus.Pending)
        {
            throw new InvalidOperationException("Only pending trustee invitations can transition.");
        }
    }

    private static void EnsureDraftOnly(ElectionLifecycleState lifecycleState)
    {
        if (lifecycleState != ElectionLifecycleState.Draft)
        {
            throw new InvalidOperationException("Trustee invitation transitions are only valid while the election remains in draft.");
        }
    }

    private void EnsureResolvedRevisionIsValid(int resolvedAtDraftRevision)
    {
        if (resolvedAtDraftRevision < SentAtDraftRevision)
        {
            throw new InvalidOperationException("Trustee invitation transitions cannot resolve before the sent draft revision.");
        }
    }
}

public record ElectionGovernedProposalRecord(
    Guid Id,
    ElectionId ElectionId,
    ElectionGovernedActionType ActionType,
    ElectionLifecycleState LifecycleStateAtCreation,
    string ProposedByPublicAddress,
    DateTime CreatedAt,
    ElectionGovernedProposalExecutionStatus ExecutionStatus,
    DateTime? LastExecutionAttemptedAt,
    DateTime? ExecutedAt,
    string? ExecutionFailureReason,
    string? LastExecutionTriggeredByPublicAddress,
    Guid? LatestTransactionId,
    long? LatestBlockHeight,
    Guid? LatestBlockId)
{
    public bool IsPending => ExecutionStatus != ElectionGovernedProposalExecutionStatus.ExecutionSucceeded;

    public bool CanRetry => ExecutionStatus == ElectionGovernedProposalExecutionStatus.ExecutionFailed;

    public ElectionGovernedProposalRecord RecordExecutionSuccess(
        DateTime executedAt,
        string? executionTriggeredByPublicAddress = null,
        Guid? latestTransactionId = null,
        long? latestBlockHeight = null,
        Guid? latestBlockId = null)
    {
        EnsureNotSucceeded();

        return this with
        {
            ExecutionStatus = ElectionGovernedProposalExecutionStatus.ExecutionSucceeded,
            LastExecutionAttemptedAt = executedAt,
            ExecutedAt = executedAt,
            ExecutionFailureReason = null,
            LastExecutionTriggeredByPublicAddress = NormalizeOptionalText(executionTriggeredByPublicAddress),
            LatestTransactionId = latestTransactionId,
            LatestBlockHeight = latestBlockHeight,
            LatestBlockId = latestBlockId,
        };
    }

    public ElectionGovernedProposalRecord RecordExecutionFailure(
        string failureReason,
        DateTime attemptedAt,
        string? executionTriggeredByPublicAddress = null,
        Guid? latestTransactionId = null,
        long? latestBlockHeight = null,
        Guid? latestBlockId = null)
    {
        EnsureNotSucceeded();

        if (string.IsNullOrWhiteSpace(failureReason))
        {
            throw new ArgumentException("Execution failure reason is required.", nameof(failureReason));
        }

        return this with
        {
            ExecutionStatus = ElectionGovernedProposalExecutionStatus.ExecutionFailed,
            LastExecutionAttemptedAt = attemptedAt,
            ExecutedAt = null,
            ExecutionFailureReason = failureReason.Trim(),
            LastExecutionTriggeredByPublicAddress = NormalizeOptionalText(executionTriggeredByPublicAddress),
            LatestTransactionId = latestTransactionId,
            LatestBlockHeight = latestBlockHeight,
            LatestBlockId = latestBlockId,
        };
    }

    private void EnsureNotSucceeded()
    {
        if (ExecutionStatus == ElectionGovernedProposalExecutionStatus.ExecutionSucceeded)
        {
            throw new InvalidOperationException("Completed governed proposals cannot be executed again.");
        }
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public record ElectionGovernedProposalApprovalRecord(
    Guid Id,
    Guid ProposalId,
    ElectionId ElectionId,
    ElectionGovernedActionType ActionType,
    ElectionLifecycleState LifecycleStateAtProposalCreation,
    string TrusteeUserAddress,
    string? TrusteeDisplayName,
    string? ApprovalNote,
    DateTime ApprovedAt,
    Guid? SourceTransactionId,
    long? SourceBlockHeight,
    Guid? SourceBlockId);
