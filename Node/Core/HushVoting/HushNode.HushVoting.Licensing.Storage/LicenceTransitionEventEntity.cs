namespace HushNode.HushVoting.Licensing.Storage;

/// <summary>
/// Immutable append-only transition evidence. Every creation, supersession, and expiry
/// writes its event in the same database transaction as the row change. Events can
/// never be updated or deleted.
/// </summary>
public sealed class LicenceTransitionEventEntity
{
    public Guid LicenceTransitionEventId { get; set; }

    public Guid LicenceSubjectId { get; set; }
    public LicenceSubjectEntity? LicenceSubject { get; set; }

    /// <summary>Monotonic per-subject event sequence (1,2,3,...).</summary>
    public long EventSequence { get; set; }

    /// <summary>Closed event type: created | superseded | expired.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Subject entitlement revision after this event committed.</summary>
    public long SubjectRevision { get; set; }

    public Guid? AssignmentId { get; set; }
    public LicenceAssignmentEntity? Assignment { get; set; }

    public string PlanId { get; set; } = string.Empty;
    public string CatalogueDecisionVersion { get; set; } = string.Empty;

    /// <summary>Source (creation) or stable reason (supersession/expiry) for the event.</summary>
    public string? SourceOrReason { get; set; }

    /// <summary>Activation operation reference when this event resulted from one.</summary>
    public Guid? OperationReferenceId { get; set; }

    public DateTime OccurredAtUtc { get; set; }
}
