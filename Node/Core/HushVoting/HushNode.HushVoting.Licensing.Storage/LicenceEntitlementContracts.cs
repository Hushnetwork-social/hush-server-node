namespace HushNode.HushVoting.Licensing.Storage;

/// <summary>
/// A trusted, server-created licence subject. Only the host identity/chain adapter (Phase 6)
/// constructs one from authenticated authority; it is never constructible from an untrusted raw
/// client subject id. The address carried here is already canonical (trimmed + invariant-lower)
/// and is the only place a raw signing address may appear in the whole feature.
/// </summary>
public sealed record AuthenticatedIdentitySubject
{
    /// <summary>Maximum UTF-8 byte length of the canonical public signing address (schema bound varchar(160)).</summary>
    public const int MaxCanonicalAddressUtf8Bytes = 160;

    public const string ErrorInvalidSubjectType = "invalid_subject_type";
    public const string ErrorInvalidAddress = "invalid_canonical_address";
    public const string ErrorNegativeCreationBlock = "negative_identity_creation_block";

    private AuthenticatedIdentitySubject(
        string subjectType,
        string canonicalPublicSigningAddress,
        long identityCreationBlockIndex)
    {
        SubjectType = subjectType;
        CanonicalPublicSigningAddress = canonicalPublicSigningAddress;
        IdentityCreationBlockIndex = identityCreationBlockIndex;
    }

    /// <summary>Closed subject type; the only v1 value is <see cref="LicencePersistenceVocabulary.SubjectTypeIdentity"/>.</summary>
    public string SubjectType { get; }

    /// <summary>Canonical normalized HushNetwork public signing address (never re-emitted off the subject row).</summary>
    public string CanonicalPublicSigningAddress { get; }

    /// <summary>Authoritative identity creation block index used for lazy-migration provenance.</summary>
    public long IdentityCreationBlockIndex { get; }

    /// <summary>
    /// Validates and constructs a trusted subject. Returns a stable error code when the supplied
    /// values are not canonical/bounded. Never coerces an invalid raw address to a valid one.
    /// </summary>
    public static bool TryCreate(
        string? subjectType,
        string? rawPublicSigningAddress,
        long identityCreationBlockIndex,
        out AuthenticatedIdentitySubject? subject,
        out string? stableErrorCode)
    {
        subject = null;
        stableErrorCode = null;

        if (subjectType is null
            || !string.Equals(subjectType, LicencePersistenceVocabulary.SubjectTypeIdentity, StringComparison.Ordinal))
        {
            stableErrorCode = ErrorInvalidSubjectType;
            return false;
        }

        var canonical = NormalizeCanonicalAddress(rawPublicSigningAddress);
        if (canonical is null)
        {
            stableErrorCode = ErrorInvalidAddress;
            return false;
        }

        if (identityCreationBlockIndex < 0)
        {
            stableErrorCode = ErrorNegativeCreationBlock;
            return false;
        }

        subject = new AuthenticatedIdentitySubject(subjectType, canonical, identityCreationBlockIndex);
        return true;
    }

    /// <summary>
    /// Canonical normalization for HushNetwork signing addresses: trim + invariant lowercase,
    /// non-empty and within the schema bound. Null when the address is not acceptable so the
    /// caller never stores a non-canonical raw value.
    /// </summary>
    public static string? NormalizeCanonicalAddress(string? rawAddress)
    {
        if (string.IsNullOrWhiteSpace(rawAddress))
        {
            return null;
        }

        var trimmed = rawAddress.Trim();
        var canonical = trimmed.ToLowerInvariant();
        if (canonical.Length == 0
            || System.Text.Encoding.UTF8.GetByteCount(canonical) > MaxCanonicalAddressUtf8Bytes)
        {
            return null;
        }

        return canonical;
    }
}

/// <summary>
/// Closed v1 resolution outcome for <c>GetOrProvision</c>. Values mirror the FeatureDescription
/// stable outcome strings and are culture-independent.
/// </summary>
public enum LicenceResolutionOutcome
{
    /// <summary>An unexpired effective assignment already existed and no state changed.</summary>
    ResolvedExisting = 0,

    /// <summary>Direct Free was created for an identity created after the rollout watermark.</summary>
    ProvisionedDefault = 1,

    /// <summary>Direct Free was lazily created for an identity created at or before the rollout watermark.</summary>
    ProvisionedMigrationDefault = 2,

    /// <summary>An annual assignment expired and Direct Free became effective in the same transaction.</summary>
    ExpiredToDefault = 3,

    /// <summary>Authority could not be established (storage unavailable / rollout not initialized). No entitlement invented.</summary>
    StorageUnavailable = 4,
}

/// <summary>
/// Closed v1 outcome for a higher-plan activation. Covers every durable database-evaluated result,
/// idempotency mismatch, and authority/availability failure. Never an exception message.
/// </summary>
public enum LicenceActivationOutcome
{
    Activated = 0,
    TransitionUnchanged = 1,
    TransitionNotHigher = 2,
    PlanUnknown = 3,
    PlanUnavailable = 4,
    PreconditionConflict = 5,
    EntitlementNotInitialized = 6,
    IdempotencyPayloadMismatch = 7,
    StorageUnavailable = 8,
    ConcurrencyExhausted = 9,
    PersistenceInvariantViolation = 10,
}

/// <summary>
/// Stable wire/name helpers for entitlement outcomes. Used for telemetry labels, structured log
/// codes, and evidence. Never contains an identity, assignment, or operation value.
/// </summary>
public static class LicenceEntitlementOutcomeNames
{
    public static string ToWireName(LicenceResolutionOutcome outcome) => outcome switch
    {
        LicenceResolutionOutcome.ResolvedExisting => "resolved_existing",
        LicenceResolutionOutcome.ProvisionedDefault => "provisioned_default",
        LicenceResolutionOutcome.ProvisionedMigrationDefault => "provisioned_migration_default",
        LicenceResolutionOutcome.ExpiredToDefault => "expired_to_default",
        LicenceResolutionOutcome.StorageUnavailable => "storage_unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown resolution outcome."),
    };

    public static string ToWireName(LicenceActivationOutcome outcome) => outcome switch
    {
        LicenceActivationOutcome.Activated => "activated",
        LicenceActivationOutcome.TransitionUnchanged => "transition_unchanged",
        LicenceActivationOutcome.TransitionNotHigher => "transition_not_higher",
        LicenceActivationOutcome.PlanUnknown => "plan_unknown",
        LicenceActivationOutcome.PlanUnavailable => "plan_unavailable",
        LicenceActivationOutcome.PreconditionConflict => "precondition_conflict",
        LicenceActivationOutcome.EntitlementNotInitialized => "entitlement_not_initialized",
        LicenceActivationOutcome.IdempotencyPayloadMismatch => "idempotency_payload_mismatch",
        LicenceActivationOutcome.StorageUnavailable => "storage_unavailable",
        LicenceActivationOutcome.ConcurrencyExhausted => "concurrency_exhausted",
        LicenceActivationOutcome.PersistenceInvariantViolation => "persistence_invariant_violation",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown activation outcome."),
    };

    /// <summary>Whether the outcome is one of the durable database-evaluated operation results.</summary>
    public static bool IsDurableOperationResult(LicenceActivationOutcome outcome) => outcome switch
    {
        LicenceActivationOutcome.Activated
            or LicenceActivationOutcome.TransitionUnchanged
            or LicenceActivationOutcome.TransitionNotHigher
            or LicenceActivationOutcome.PlanUnknown
            or LicenceActivationOutcome.PlanUnavailable
            or LicenceActivationOutcome.PreconditionConflict
            or LicenceActivationOutcome.EntitlementNotInitialized => true,
        _ => false,
    };

    public static LicenceActivationOutcome FromDurableResultString(string durableResult)
    {
        ArgumentNullException.ThrowIfNull(durableResult);
        return durableResult switch
        {
            LicencePersistenceVocabulary.OperationResultActivated => LicenceActivationOutcome.Activated,
            LicencePersistenceVocabulary.OperationResultTransitionUnchanged => LicenceActivationOutcome.TransitionUnchanged,
            LicencePersistenceVocabulary.OperationResultTransitionNotHigher => LicenceActivationOutcome.TransitionNotHigher,
            LicencePersistenceVocabulary.OperationResultPlanUnknown => LicenceActivationOutcome.PlanUnknown,
            LicencePersistenceVocabulary.OperationResultPlanUnavailable => LicenceActivationOutcome.PlanUnavailable,
            LicencePersistenceVocabulary.OperationResultPreconditionConflict => LicenceActivationOutcome.PreconditionConflict,
            LicencePersistenceVocabulary.OperationResultEntitlementNotInitialized => LicenceActivationOutcome.EntitlementNotInitialized,
            _ => throw new ArgumentOutOfRangeException(nameof(durableResult), durableResult, "Unknown durable operation result."),
        };
    }
}

/// <summary>
/// Authoritative effective-entitlement projection returned by the internal service. Stable typed
/// projection for downstream FEAT-014/015/018; never a persistence entity and never a raw identity.
/// </summary>
public sealed record EffectiveLicenceEntitlement(
    Guid LicenceSubjectId,
    Guid LicenceAssignmentId,
    string PlanId,
    string PlanFamily,
    int UpgradeRank,
    int? EligibleVoterCap,
    bool UnlimitedElectionPolicy,
    string TermKind,
    int TermYears,
    IReadOnlyList<string> AllowedGovernanceOptionIds,
    string Source,
    DateTime EffectiveFromUtc,
    DateTime? ExpiresAtUtc,
    string AssignedCatalogueVersion,
    string AssignedCatalogueDigestSha256,
    long EntitlementRevision);

/// <summary>Result of <c>GetOrProvision</c>. Business and authority outcomes are typed, never exceptions.</summary>
public sealed record LicenceResolutionResult(
    bool IsSuccess,
    LicenceResolutionOutcome? Outcome,
    EffectiveLicenceEntitlement? Entitlement,
    string? StableErrorCode,
    string? SafeErrorReason)
{
    public static LicenceResolutionResult Ok(
        LicenceResolutionOutcome outcome,
        EffectiveLicenceEntitlement entitlement) =>
        new(true, outcome, entitlement, null, null);

    public static LicenceResolutionResult Fail(string stableErrorCode, string safeErrorReason) =>
        new(false, null, null, stableErrorCode, safeErrorReason);
}

/// <summary>
/// A bounded higher-plan activation command. The caller must have previously resolved the
/// entitlement and supplies the observed current plan id and monotonic entitlement revision.
/// </summary>
public sealed record LicenceActivationCommand(
    Guid IdempotencyKey,
    string ExpectedCurrentPlanId,
    long ExpectedEntitlementRevision,
    string RequestedTargetPlanId,
    string? RequestCorrelationId = null)
{
    /// <summary>Maximum UTF-8 byte length of plan ids on the command boundary (schema bound varchar(64)).</summary>
    public const int MaxPlanIdUtf8Bytes = 64;

    public const string ErrorInvalidIdempotencyKey = "invalid_idempotency_key";
    public const string ErrorInvalidExpectedPlan = "invalid_expected_current_plan";
    public const string ErrorInvalidTargetPlan = "invalid_requested_target_plan";
    public const string ErrorNegativeExpectedRevision = "negative_expected_revision";
    public const string ErrorCorrelationTooLong = "correlation_id_too_long";

    public static bool TryCreate(
        Guid idempotencyKey,
        string? expectedCurrentPlanId,
        long expectedEntitlementRevision,
        string? requestedTargetPlanId,
        string? requestCorrelationId,
        out LicenceActivationCommand? command,
        out string? stableErrorCode)
    {
        command = null;
        stableErrorCode = null;

        if (idempotencyKey == Guid.Empty)
        {
            stableErrorCode = ErrorInvalidIdempotencyKey;
            return false;
        }

        if (!IsBoundedPlanId(expectedCurrentPlanId))
        {
            stableErrorCode = ErrorInvalidExpectedPlan;
            return false;
        }

        if (expectedEntitlementRevision < 0)
        {
            stableErrorCode = ErrorNegativeExpectedRevision;
            return false;
        }

        if (!IsBoundedPlanId(requestedTargetPlanId))
        {
            stableErrorCode = ErrorInvalidTargetPlan;
            return false;
        }

        var correlation = requestCorrelationId?.Trim();
        if (correlation is not null
            && System.Text.Encoding.UTF8.GetByteCount(correlation) > 96)
        {
            stableErrorCode = ErrorCorrelationTooLong;
            return false;
        }

        command = new LicenceActivationCommand(
            idempotencyKey,
            expectedCurrentPlanId!.Trim(),
            expectedEntitlementRevision,
            requestedTargetPlanId!.Trim(),
            correlation);
        return true;
    }

    private static bool IsBoundedPlanId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed.Length > 0
            && System.Text.Encoding.UTF8.GetByteCount(trimmed) <= MaxPlanIdUtf8Bytes;
    }
}

/// <summary>Result of a higher-plan activation. Typed closed outcomes only.</summary>
public sealed record LicenceActivationResult(
    bool IsSuccess,
    LicenceActivationOutcome? Outcome,
    EffectiveLicenceEntitlement? Entitlement,
    string? StableErrorCode,
    string? SafeErrorReason)
{
    public static LicenceActivationResult Ok(
        LicenceActivationOutcome outcome,
        EffectiveLicenceEntitlement? entitlement,
        string? safeReason = null) =>
        new(true, outcome, entitlement, null, safeReason);

    public static LicenceActivationResult Fail(string stableErrorCode, string safeErrorReason) =>
        new(false, null, null, stableErrorCode, safeErrorReason);
}

/// <summary>
/// Stable code strings for entitlement authority/availability failures shared by both operations.
/// These are the closed v1 stable result vocabulary entries used at the internal service boundary.
/// </summary>
public static class LicenceEntitlementFailureCodes
{
    public const string StorageUnavailable = "storage_unavailable";
    public const string ConcurrencyExhausted = "concurrency_exhausted";
    public const string PersistenceInvariantViolation = "persistence_invariant_violation";
    public const string CatalogueIncompatible = "catalogue_incompatible";
}

/// <summary>
/// Typed signal for a recognized transient PostgreSQL race (unique first-insert, serialization,
/// deadlock). The bounded executor retries these; anything else is never retried.
/// </summary>
public sealed class LicenceTransientConflictException : Exception
{
    public LicenceTransientConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Typed signal that a commit outcome is unknown (response lost or connection failed at the commit
/// boundary). The bounded executor reconciles against the database before any new mutation.
/// </summary>
public sealed class LicenceAmbiguousCommitException : Exception
{
    public LicenceAmbiguousCommitException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Explicit failure-injection seam required by the delivery evidence (transient and ambiguous-commit
/// cases must be provable deterministically). Production callers pass null; TwinTests/failure
/// tests pass non-null hooks. Hooks receive the 1-based attempt number so a test can fail exactly
/// one attempt. Hooks never carry identity or plan data into logs or results.
/// </summary>
public sealed record LicenceFailureInjection
{
    /// <summary>Invoked before each single attempt (1-based attempt number).</summary>
    public Func<int, CancellationToken, Task>? BeforeAttemptAsync { get; init; }

    /// <summary>Invoked immediately before COMMIT; throwing models a lost/unknown commit (absent case).</summary>
    public Func<int, CancellationToken, Task>? BeforeCommitAsync { get; init; }

    /// <summary>Invoked after a successful COMMIT; throwing models a lost success response (committed case).</summary>
    public Func<int, CancellationToken, Task>? AfterCommitAsync { get; init; }

    /// <summary>Diagnostic counter hook (test evidence); production callers never set it.</summary>
    public Func<int>? AttemptCounter { get; init; }
}
