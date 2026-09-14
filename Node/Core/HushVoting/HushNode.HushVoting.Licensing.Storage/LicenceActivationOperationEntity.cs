namespace HushNode.HushVoting.Licensing.Storage;

/// <summary>
/// One durable activation operation. Idempotency records are retained indefinitely.
/// Rejected activation attempts are operation records, never transition events.
/// </summary>
public sealed class LicenceActivationOperationEntity
{
    public Guid LicenceActivationOperationId { get; set; }

    public Guid LicenceSubjectId { get; set; }
    public LicenceSubjectEntity? LicenceSubject { get; set; }

    /// <summary>Caller-generated canonical UUID idempotency key, unique per licence subject.</summary>
    public Guid IdempotencyKey { get; set; }

    /// <summary>SHA-256 fingerprint of the canonical command payload (uppercase hex).</summary>
    public string CanonicalPayloadFingerprintSha256 { get; set; } = string.Empty;

    public string ExpectedCurrentPlanId { get; set; } = string.Empty;
    public long ExpectedEntitlementRevision { get; set; }
    public string RequestedTargetPlanId { get; set; } = string.Empty;
    public string EvaluatedCatalogueVersion { get; set; } = string.Empty;

    /// <summary>
    /// Durable database-evaluated business outcome (closed v1 set:
    /// activated | transition_unchanged | transition_not_higher | plan_unknown |
    /// plan_unavailable | precondition_conflict | entitlement_not_initialized).
    /// </summary>
    public string? DurableResult { get; set; }

    public Guid? ResultingAssignmentId { get; set; }
    public LicenceAssignmentEntity? ResultingAssignment { get; set; }
    public long? ResultingEntitlementRevision { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>Privacy-safe request correlation; never a raw identity.</summary>
    public string? RequestCorrelationId { get; set; }
}
