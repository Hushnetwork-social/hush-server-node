// FEAT-015 Task 2.3 — stable licence transaction validation-code registry.
//
// The exact 20 uppercase codes from the FeatureDescription "Licence transaction
// validation codes" section (normative). Codes are stable typed identifiers for
// `SubmitSignedTransactionReply.ValidationCode`, telemetry, and safe diagnostics;
// they never contain message text, identities, plan values, or signatures.
// Unknown codes fail closed (never shown verbatim, never coerced to another code).

namespace HushNode.HushVoting.Licence.Transactions;

/// <summary>
/// Stable licence validation-code registry. The set is closed; every code string is
/// pinned by contract tests against the FeatureDescription list verbatim.
/// </summary>
public static class HushVotingLicenceValidationCodes
{
    // Payload/shape failures.
    public const string PayloadKindUnsupported = "LICENCE_PAYLOAD_KIND_UNSUPPORTED";
    public const string PayloadMalformed = "LICENCE_PAYLOAD_MALFORMED";
    public const string PayloadSizeMismatch = "LICENCE_PAYLOAD_SIZE_MISMATCH";

    // Signature/identity failures.
    public const string SignatureInvalid = "LICENCE_SIGNATURE_INVALID";
    public const string SignatoryIdentityNotFound = "LICENCE_SIGNATORY_IDENTITY_NOT_FOUND";

    // Intent/catalogue/transition-target failures.
    public const string IntentUnknown = "LICENCE_INTENT_UNKNOWN";
    public const string PlanUnknown = "LICENCE_PLAN_UNKNOWN";
    public const string PlanUnavailable = "LICENCE_PLAN_UNAVAILABLE";
    public const string EnterpriseAdminOnly = "LICENCE_ENTERPRISE_ADMIN_ONLY";
    public const string CatalogueStale = "LICENCE_CATALOGUE_STALE";

    // Precondition/transition-rule failures.
    public const string BaselineRequiresNoActiveEntitlement = "LICENCE_BASELINE_REQUIRES_NO_ACTIVE_ENTITLEMENT";
    public const string UpgradeRequiresActiveEntitlement = "LICENCE_UPGRADE_REQUIRES_ACTIVE_ENTITLEMENT";
    public const string ExpectedCurrentInvalid = "LICENCE_EXPECTED_CURRENT_INVALID";
    public const string PreconditionStale = "LICENCE_PRECONDITION_STALE";
    public const string TransitionUnchanged = "LICENCE_TRANSITION_UNCHANGED";
    public const string TransitionNotHigher = "LICENCE_TRANSITION_NOT_HIGHER";

    // Admission/idempotency/concurrency failures.
    public const string TransitionPending = "LICENCE_TRANSITION_PENDING";
    public const string TransactionIdempotencyMismatch = "LICENCE_TRANSACTION_IDEMPOTENCY_MISMATCH";

    // Infrastructure/index failures.
    public const string IndexAuthorityUnavailable = "LICENCE_INDEX_AUTHORITY_UNAVAILABLE";
    public const string PersistenceInvariantViolation = "LICENCE_PERSISTENCE_INVARIANT_VIOLATION";

    /// <summary>The exact closed set of stable licence validation codes (FeatureDescription list, verbatim).</summary>
    public static readonly IReadOnlyList<string> Known = new[]
    {
        PayloadKindUnsupported,
        PayloadMalformed,
        PayloadSizeMismatch,
        SignatureInvalid,
        SignatoryIdentityNotFound,
        IntentUnknown,
        PlanUnknown,
        PlanUnavailable,
        EnterpriseAdminOnly,
        CatalogueStale,
        BaselineRequiresNoActiveEntitlement,
        UpgradeRequiresActiveEntitlement,
        ExpectedCurrentInvalid,
        PreconditionStale,
        TransitionUnchanged,
        TransitionNotHigher,
        TransitionPending,
        TransactionIdempotencyMismatch,
        IndexAuthorityUnavailable,
        PersistenceInvariantViolation,
    };

    private static readonly HashSet<string> KnownSet = new(Known, StringComparer.Ordinal);

    /// <summary>True only for the exact closed code set; unknown codes fail closed.</summary>
    public static bool IsKnown(string? code) =>
        code is not null && KnownSet.Contains(code);
}
