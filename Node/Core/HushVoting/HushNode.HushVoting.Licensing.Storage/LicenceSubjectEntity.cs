namespace HushNode.HushVoting.Licensing.Storage;

/// <summary>
/// One durable entitled subject per canonical authenticated identity. This is the
/// ONLY row that stores the raw public signing address; every other record uses the
/// internal subject identifier or a privacy-safe correlation value.
/// </summary>
public sealed class LicenceSubjectEntity
{
    public Guid LicenceSubjectId { get; set; }

    /// <summary>Closed subject type; the only v1 value is <see cref="LicencePersistenceVocabulary.SubjectTypeIdentity"/>.</summary>
    public string SubjectType { get; set; } = LicencePersistenceVocabulary.SubjectTypeIdentity;

    /// <summary>Canonical normalized HushNetwork public signing address. Raw identity: never repeated off this row.</summary>
    public string CanonicalPublicSigningAddress { get; set; } = string.Empty;

    /// <summary>Authoritative identity creation block index (rollout provenance).</summary>
    public long IdentityCreationBlockIndex { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Monotonic entitlement revision: 0 before the first effective assignment, 1 after
    /// the first state-changing transaction, +1 exactly once per later committed
    /// state-changing transaction (even when that transaction writes multiple rows).
    /// </summary>
    public long EntitlementRevision { get; set; }

    public ICollection<LicenceAssignmentEntity> Assignments { get; set; } = new List<LicenceAssignmentEntity>();
    public ICollection<LicenceTransitionEventEntity> TransitionEvents { get; set; } = new List<LicenceTransitionEventEntity>();
    public ICollection<LicenceActivationOperationEntity> ActivationOperations { get; set; } = new List<LicenceActivationOperationEntity>();
    public ICollection<LicencePendingReservationEntity> PendingReservations { get; set; } = new List<LicencePendingReservationEntity>();
}
