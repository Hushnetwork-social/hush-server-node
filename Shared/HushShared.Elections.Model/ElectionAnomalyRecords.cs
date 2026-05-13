namespace HushShared.Elections.Model;

public record ElectionAnomalyThreadRecord(
    Guid Id,
    ElectionId ElectionId,
    string SubmitterPersonScopeId,
    string SubmitterPersonScopeDerivationVersion,
    string SubmitterActorPublicAddress,
    string? SubmitterRoleContextId,
    string SubmitterRoleEvidenceTypeId,
    string SubmitterRoleEvidenceReference,
    ElectionLifecycleState LifecycleStateAtSubmission,
    DateTime? SubmissionWindowClosesAt,
    string CurrentCategoryId,
    string CurrentCaseStateId,
    string? SeverityCandidateId,
    string? GovernedDecisionRef,
    bool HasOpenClarificationRequest,
    Guid? OpenClarificationRequestId,
    DateTime CreatedAt,
    DateTime LastUpdatedAt,
    Guid SourceTransactionId,
    long? SourceBlockHeight,
    Guid? SourceBlockId,
    string CurrentThreadHash);

public record ElectionAnomalyThreadEventRecord(
    Guid Id,
    Guid AnomalyThreadId,
    ElectionId ElectionId,
    int Sequence,
    string EventTypeId,
    string EventPayloadJson,
    string EventHash,
    string? PreviousEventHash,
    Guid ActionNonce,
    Guid SourceTransactionId,
    long? SourceBlockHeight,
    Guid? SourceBlockId,
    string ActorPublicAddress,
    DateTime OccurredAt);

public record ElectionAnomalyMessageEnvelopeRecord(
    Guid Id,
    Guid AnomalyThreadId,
    Guid EventId,
    ElectionId ElectionId,
    string MessageKindId,
    string EncryptedBody,
    string EncryptedBodyHash,
    string? PlaintextBodyHash,
    int PlaintextCharacterCount,
    string? EncryptionAlgorithm,
    DateTime RecordedAt);

public record ElectionAnomalyRecipientWrapRecord(
    Guid Id,
    Guid MessageEnvelopeId,
    Guid AnomalyThreadId,
    ElectionId ElectionId,
    string RecipientRoleId,
    string RecipientPublicAddress,
    string RecipientKeyFingerprint,
    string EncryptedContentKey,
    string WrapAlgorithm,
    string WrapStatusId,
    DateTime RecordedAt);

public record ElectionAnomalyActionRecord(
    Guid Id,
    ElectionId ElectionId,
    Guid? AnomalyThreadId,
    Guid? ActionNonce,
    string ActionType,
    string ActionOutcomeId,
    string ActorPublicAddress,
    string? ValidationCode,
    string? DiagnosticReference,
    Guid SourceTransactionId,
    long? SourceBlockHeight,
    Guid? SourceBlockId,
    DateTime RecordedAt);
