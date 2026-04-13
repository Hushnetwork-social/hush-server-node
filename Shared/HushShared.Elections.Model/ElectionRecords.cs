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
    OfficialResultVisibilityPolicy OfficialResultVisibilityPolicy,
    int CurrentDraftRevision,
    IReadOnlyList<ElectionOptionDefinition> Options,
    IReadOnlyList<ElectionWarningCode> AcknowledgedWarningCodes,
    int? RequiredApprovalCount,
    DateTime CreatedAt,
    DateTime LastUpdatedAt,
    DateTime? OpenedAt,
    DateTime? VoteAcceptanceLockedAt,
    DateTime? ClosedAt,
    DateTime? FinalizedAt,
    DateTime? TallyReadyAt,
    ElectionClosedProgressStatus ClosedProgressStatus,
    Guid? OpenArtifactId,
    Guid? CloseArtifactId,
    Guid? TallyReadyArtifactId,
    Guid? FinalizeArtifactId,
    Guid? UnofficialResultArtifactId,
    Guid? OfficialResultArtifactId);

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
    ElectionCeremonyBindingSnapshot? CeremonySnapshot,
    byte[]? FrozenEligibleVoterSetHash,
    string? TrusteePolicyExecutionReference,
    string? ReportingPolicyExecutionReference,
    string? ReviewWindowExecutionReference,
    int? AcceptedBallotCount,
    byte[]? AcceptedBallotSetHash,
    int? PublishedBallotCount,
    byte[]? PublishedBallotStreamHash,
    byte[]? FinalEncryptedTallyHash,
    DateTime RecordedAt,
    string RecordedByPublicAddress,
    Guid? SourceTransactionId,
    long? SourceBlockHeight,
    Guid? SourceBlockId);

public record ElectionAcceptedBallotRecord(
    Guid Id,
    ElectionId ElectionId,
    string EncryptedBallotPackage,
    string ProofBundle,
    string BallotNullifier,
    DateTime AcceptedAt)
{
    public string EncryptedBallotPackage { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            EncryptedBallotPackage,
            nameof(EncryptedBallotPackage));

    public string ProofBundle { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            ProofBundle,
            nameof(ProofBundle));

    public string BallotNullifier { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            BallotNullifier,
            nameof(BallotNullifier));
}

public record ElectionBallotMemPoolRecord(
    Guid Id,
    ElectionId ElectionId,
    Guid AcceptedBallotId,
    DateTime QueuedAt);

public record ElectionPublishedBallotRecord(
    Guid Id,
    ElectionId ElectionId,
    long PublicationSequence,
    string EncryptedBallotPackage,
    string ProofBundle,
    DateTime PublishedAt,
    long? SourceBlockHeight,
    Guid? SourceBlockId)
{
    public string EncryptedBallotPackage { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            EncryptedBallotPackage,
            nameof(EncryptedBallotPackage));

    public string ProofBundle { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            ProofBundle,
            nameof(ProofBundle));
}

public record ElectionCastIdempotencyRecord(
    ElectionId ElectionId,
    string IdempotencyKeyHash,
    DateTime RecordedAt)
{
    public string IdempotencyKeyHash { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            IdempotencyKeyHash,
            nameof(IdempotencyKeyHash));
}

public record ElectionPublicationIssueRecord(
    Guid Id,
    ElectionId ElectionId,
    ElectionPublicationIssueCode IssueCode,
    int OccurrenceCount,
    DateTime FirstObservedAt,
    DateTime LastObservedAt,
    long? LatestBlockHeight,
    Guid? LatestBlockId)
{
    public ElectionPublicationIssueRecord RegisterOccurrence(
        DateTime observedAt,
        long? latestBlockHeight,
        Guid? latestBlockId) =>
        this with
        {
            OccurrenceCount = OccurrenceCount + 1,
            LastObservedAt = observedAt,
            LatestBlockHeight = latestBlockHeight,
            LatestBlockId = latestBlockId,
        };
}

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

public record ElectionCeremonyBindingSnapshot(
    Guid CeremonyVersionId,
    int CeremonyVersionNumber,
    string ProfileId,
    int BoundTrusteeCount,
    int RequiredApprovalCount,
    IReadOnlyList<ElectionTrusteeReference> ActiveTrustees,
    bool EveryActiveTrusteeMustApprove,
    string TallyPublicKeyFingerprint);

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

public record ElectionCeremonyProfileRecord(
    string ProfileId,
    string DisplayName,
    string Description,
    string ProviderKey,
    string ProfileVersion,
    int TrusteeCount,
    int RequiredApprovalCount,
    bool DevOnly,
    DateTime RegisteredAt,
    DateTime LastUpdatedAt);

public record ElectionCeremonyVersionRecord(
    Guid Id,
    ElectionId ElectionId,
    int VersionNumber,
    string ProfileId,
    ElectionCeremonyVersionStatus Status,
    int TrusteeCount,
    int RequiredApprovalCount,
    IReadOnlyList<ElectionTrusteeReference> BoundTrustees,
    string StartedByPublicAddress,
    DateTime StartedAt,
    DateTime? CompletedAt,
    DateTime? SupersededAt,
    string? SupersededReason,
    string? TallyPublicKeyFingerprint)
{
    public bool IsActive => Status != ElectionCeremonyVersionStatus.Superseded;

    public ElectionCeremonyVersionRecord MarkReady(
        DateTime completedAt,
        string tallyPublicKeyFingerprint)
    {
        EnsureInProgress();

        if (string.IsNullOrWhiteSpace(tallyPublicKeyFingerprint))
        {
            throw new ArgumentException("Tally public key fingerprint is required.", nameof(tallyPublicKeyFingerprint));
        }

        return this with
        {
            Status = ElectionCeremonyVersionStatus.Ready,
            CompletedAt = completedAt,
            TallyPublicKeyFingerprint = tallyPublicKeyFingerprint.Trim(),
        };
    }

    public ElectionCeremonyVersionRecord Supersede(
        DateTime supersededAt,
        string supersededReason)
    {
        if (Status == ElectionCeremonyVersionStatus.Superseded)
        {
            throw new InvalidOperationException("Ceremony version is already superseded.");
        }

        if (string.IsNullOrWhiteSpace(supersededReason))
        {
            throw new ArgumentException("Superseded reason is required.", nameof(supersededReason));
        }

        return this with
        {
            Status = ElectionCeremonyVersionStatus.Superseded,
            SupersededAt = supersededAt,
            SupersededReason = supersededReason.Trim(),
        };
    }

    private void EnsureInProgress()
    {
        if (Status != ElectionCeremonyVersionStatus.InProgress)
        {
            throw new InvalidOperationException("Only in-progress ceremony versions can become ready.");
        }
    }
}

public record ElectionCeremonyTranscriptEventRecord(
    Guid Id,
    ElectionId ElectionId,
    Guid CeremonyVersionId,
    int VersionNumber,
    ElectionCeremonyTranscriptEventType EventType,
    string? ActorPublicAddress,
    string? TrusteeUserAddress,
    string? TrusteeDisplayName,
    ElectionTrusteeCeremonyState? TrusteeState,
    string EventSummary,
    string? EvidenceReference,
    string? RestartReason,
    string? TallyPublicKeyFingerprint,
    DateTime OccurredAt);

public record ElectionCeremonyMessageEnvelopeRecord(
    Guid Id,
    ElectionId ElectionId,
    Guid CeremonyVersionId,
    int VersionNumber,
    string ProfileId,
    string SenderTrusteeUserAddress,
    string? RecipientTrusteeUserAddress,
    string MessageType,
    string PayloadVersion,
    byte[] EncryptedPayload,
    string PayloadFingerprint,
    DateTime SubmittedAt);

public record ElectionCeremonyTrusteeStateRecord(
    Guid Id,
    ElectionId ElectionId,
    Guid CeremonyVersionId,
    string TrusteeUserAddress,
    string? TrusteeDisplayName,
    ElectionTrusteeCeremonyState State,
    string? TransportPublicKeyFingerprint,
    DateTime? TransportPublicKeyPublishedAt,
    DateTime? JoinedAt,
    DateTime? SelfTestSucceededAt,
    DateTime? MaterialSubmittedAt,
    DateTime? ValidationFailedAt,
    string? ValidationFailureReason,
    DateTime? CompletedAt,
    DateTime? RemovedAt,
    string? ShareVersion,
    DateTime LastUpdatedAt)
{
    public bool HasPublishedTransportKey =>
        !string.IsNullOrWhiteSpace(TransportPublicKeyFingerprint) &&
        TransportPublicKeyPublishedAt.HasValue;

    public ElectionCeremonyTrusteeStateRecord PublishTransportKey(
        string transportPublicKeyFingerprint,
        DateTime publishedAt)
    {
        EnsureMutable();

        if (HasPublishedTransportKey)
        {
            throw new InvalidOperationException("Trustee has already published a transport key for this ceremony version.");
        }

        EnsureStateIn(ElectionTrusteeCeremonyState.AcceptedTrustee, ElectionTrusteeCeremonyState.CeremonyNotStarted);

        if (string.IsNullOrWhiteSpace(transportPublicKeyFingerprint))
        {
            throw new ArgumentException("Transport public key fingerprint is required.", nameof(transportPublicKeyFingerprint));
        }

        return this with
        {
            TransportPublicKeyFingerprint = transportPublicKeyFingerprint.Trim(),
            TransportPublicKeyPublishedAt = publishedAt,
            LastUpdatedAt = publishedAt,
        };
    }

    public ElectionCeremonyTrusteeStateRecord MarkJoined(DateTime joinedAt)
    {
        EnsureMutable();

        if (JoinedAt.HasValue)
        {
            throw new InvalidOperationException("Trustee has already joined this ceremony version.");
        }

        EnsureTransportKeyPublished();
        EnsureStateIn(ElectionTrusteeCeremonyState.AcceptedTrustee, ElectionTrusteeCeremonyState.CeremonyNotStarted);

        return this with
        {
            State = ElectionTrusteeCeremonyState.CeremonyJoined,
            JoinedAt = joinedAt,
            LastUpdatedAt = joinedAt,
        };
    }

    public ElectionCeremonyTrusteeStateRecord RecordSelfTestSuccess(DateTime succeededAt)
    {
        EnsureMutable();

        if (SelfTestSucceededAt.HasValue && State != ElectionTrusteeCeremonyState.CeremonyValidationFailed)
        {
            throw new InvalidOperationException("Trustee self-test has already been recorded for this ceremony version.");
        }

        if (!JoinedAt.HasValue)
        {
            throw new InvalidOperationException("Trustee must join the ceremony before recording a successful self-test.");
        }

        EnsureStateIn(ElectionTrusteeCeremonyState.CeremonyJoined, ElectionTrusteeCeremonyState.CeremonyValidationFailed);

        return this with
        {
            State = ElectionTrusteeCeremonyState.CeremonyJoined,
            SelfTestSucceededAt = succeededAt,
            MaterialSubmittedAt = null,
            ShareVersion = null,
            ValidationFailedAt = null,
            ValidationFailureReason = null,
            LastUpdatedAt = succeededAt,
        };
    }

    public ElectionCeremonyTrusteeStateRecord RecordMaterialSubmitted(DateTime submittedAt, string shareVersion)
    {
        EnsureMutable();

        if (MaterialSubmittedAt.HasValue && State != ElectionTrusteeCeremonyState.CeremonyValidationFailed)
        {
            throw new InvalidOperationException("Trustee ceremony material has already been submitted for this version.");
        }

        if (!JoinedAt.HasValue)
        {
            throw new InvalidOperationException("Trustee must join the ceremony before submitting material.");
        }

        EnsureStateIn(ElectionTrusteeCeremonyState.CeremonyJoined, ElectionTrusteeCeremonyState.CeremonyValidationFailed);

        if (!SelfTestSucceededAt.HasValue)
        {
            throw new InvalidOperationException("Trustee material cannot be submitted before a successful self-test.");
        }

        if (string.IsNullOrWhiteSpace(shareVersion))
        {
            throw new ArgumentException("Share version is required.", nameof(shareVersion));
        }

        return this with
        {
            State = ElectionTrusteeCeremonyState.CeremonyMaterialSubmitted,
            MaterialSubmittedAt = submittedAt,
            ShareVersion = shareVersion.Trim(),
            LastUpdatedAt = submittedAt,
        };
    }

    public ElectionCeremonyTrusteeStateRecord RecordValidationFailure(
        string validationFailureReason,
        DateTime failedAt)
    {
        EnsureMutable();
        EnsureStateIn(ElectionTrusteeCeremonyState.CeremonyMaterialSubmitted);

        if (string.IsNullOrWhiteSpace(validationFailureReason))
        {
            throw new ArgumentException("Validation failure reason is required.", nameof(validationFailureReason));
        }

        return this with
        {
            State = ElectionTrusteeCeremonyState.CeremonyValidationFailed,
            SelfTestSucceededAt = null,
            MaterialSubmittedAt = null,
            ShareVersion = null,
            ValidationFailedAt = failedAt,
            ValidationFailureReason = validationFailureReason.Trim(),
            LastUpdatedAt = failedAt,
        };
    }

    public ElectionCeremonyTrusteeStateRecord MarkCompleted(
        DateTime completedAt,
        string shareVersion)
    {
        EnsureMutable();

        if (CompletedAt.HasValue)
        {
            throw new InvalidOperationException("Trustee ceremony completion has already been recorded for this version.");
        }

        if (State != ElectionTrusteeCeremonyState.CeremonyMaterialSubmitted)
        {
            throw new InvalidOperationException("Trustee material must be submitted before ceremony completion.");
        }

        if (string.IsNullOrWhiteSpace(shareVersion))
        {
            throw new ArgumentException("Share version is required.", nameof(shareVersion));
        }

        return this with
        {
            State = ElectionTrusteeCeremonyState.CeremonyCompleted,
            CompletedAt = completedAt,
            ValidationFailedAt = null,
            ValidationFailureReason = null,
            ShareVersion = shareVersion.Trim(),
            LastUpdatedAt = completedAt,
        };
    }

    public ElectionCeremonyTrusteeStateRecord MarkRemoved(DateTime removedAt)
    {
        if (State == ElectionTrusteeCeremonyState.Removed)
        {
            throw new InvalidOperationException("Trustee is already removed from this ceremony version.");
        }

        return this with
        {
            State = ElectionTrusteeCeremonyState.Removed,
            RemovedAt = removedAt,
            LastUpdatedAt = removedAt,
        };
    }

    private void EnsureMutable()
    {
        if (State == ElectionTrusteeCeremonyState.Removed)
        {
            throw new InvalidOperationException("Removed trustees cannot advance the ceremony state.");
        }
    }

    private void EnsureTransportKeyPublished()
    {
        if (!HasPublishedTransportKey)
        {
            throw new InvalidOperationException("Trustee must publish a transport key before joining the ceremony.");
        }
    }

    private void EnsureStateIn(params ElectionTrusteeCeremonyState[] allowedStates)
    {
        if (!allowedStates.Contains(State))
        {
            throw new InvalidOperationException($"Trustee state '{State}' is not valid for this transition.");
        }
    }
}

public record ElectionCeremonyShareCustodyRecord(
    Guid Id,
    ElectionId ElectionId,
    Guid CeremonyVersionId,
    string TrusteeUserAddress,
    string ShareVersion,
    bool PasswordProtected,
    ElectionCeremonyShareCustodyStatus Status,
    DateTime? LastExportedAt,
    DateTime? LastImportedAt,
    DateTime? LastImportFailedAt,
    string? LastImportFailureReason,
    DateTime LastUpdatedAt)
{
    public bool MatchesImportBinding(
        ElectionId electionId,
        Guid ceremonyVersionId,
        string trusteeUserAddress,
        string shareVersion) =>
        ElectionId == electionId &&
        CeremonyVersionId == ceremonyVersionId &&
        string.Equals(TrusteeUserAddress, trusteeUserAddress, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(ShareVersion, shareVersion, StringComparison.Ordinal);

    public ElectionCeremonyShareCustodyRecord RecordExport(DateTime exportedAt) =>
        this with
        {
            Status = ElectionCeremonyShareCustodyStatus.Exported,
            LastExportedAt = exportedAt,
            LastUpdatedAt = exportedAt,
        };

    public ElectionCeremonyShareCustodyRecord RecordImportSuccess(DateTime importedAt) =>
        this with
        {
            Status = ElectionCeremonyShareCustodyStatus.Imported,
            LastImportedAt = importedAt,
            LastImportFailedAt = null,
            LastImportFailureReason = null,
            LastUpdatedAt = importedAt,
        };

    public ElectionCeremonyShareCustodyRecord RecordImportFailure(
        string failureReason,
        DateTime failedAt)
    {
        if (string.IsNullOrWhiteSpace(failureReason))
        {
            throw new ArgumentException("Import failure reason is required.", nameof(failureReason));
        }

        return this with
        {
            Status = ElectionCeremonyShareCustodyStatus.ImportFailed,
            LastImportFailedAt = failedAt,
            LastImportFailureReason = failureReason.Trim(),
            LastUpdatedAt = failedAt,
        };
    }
}
