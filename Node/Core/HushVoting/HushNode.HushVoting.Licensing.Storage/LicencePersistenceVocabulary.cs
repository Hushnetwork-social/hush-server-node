namespace HushNode.HushVoting.Licensing.Storage;

/// <summary>
/// Closed v1 persistence vocabulary for the HushVoting licensing store.
/// Values are the exact durable strings written to PostgreSQL and enforced by
/// database CHECK constraints; never store a value outside this closed set.
/// </summary>
public static class LicencePersistenceVocabulary
{
    /// <summary>Only v1 subject type: an authenticated canonical HushNetwork identity.</summary>
    public const string SubjectTypeIdentity = "Identity";

    public const string LifecycleActive = "active";
    public const string LifecycleSuperseded = "superseded";
    public const string LifecycleExpired = "expired";

    public const string SourceDefaultFree = "default_free";
    public const string SourceMigrationLazyDefault = "migration_lazy_default";
    public const string SourceAutomaticUpgrade = "automatic_upgrade";
    public const string SourceAutomaticExpiry = "automatic_expiry";

    public const string EventTypeCreated = "created";
    public const string EventTypeSuperseded = "superseded";
    public const string EventTypeExpired = "expired";

    public const string TermPerpetual = "perpetual";
    public const string TermAnnual = "annual";

    public const string PlanFamilyDirect = "direct";
    public const string PlanFamilyVeritas = "veritas";
    public const string PlanFamilyEnterprise = "enterprise";

    // Durable database-evaluated activation operation results (closed v1 set).
    public const string OperationResultActivated = "activated";
    public const string OperationResultTransitionUnchanged = "transition_unchanged";
    public const string OperationResultTransitionNotHigher = "transition_not_higher";
    public const string OperationResultPlanUnknown = "plan_unknown";
    public const string OperationResultPlanUnavailable = "plan_unavailable";
    public const string OperationResultPreconditionConflict = "precondition_conflict";
    public const string OperationResultEntitlementNotInitialized = "entitlement_not_initialized";
}
