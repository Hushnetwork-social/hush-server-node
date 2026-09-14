namespace HushNode.HushVoting.Licensing.Storage;

/// <summary>
/// One DB-backed pending licence-transition reservation per canonical identity (FEAT-015 D4).
/// Exists from mempool admission until block indexing resolves it; unlike FEAT-013's
/// activation-operation rows, this is admission/competition state owned by the licence
/// transaction handler. Exact signed transaction bytes are pinned by the canonical payload
/// fingerprint; a reused transaction id with different bytes is an idempotency mismatch.
/// Rows are retained (append-only admission evidence); a resolved row is never deleted.
/// </summary>
public sealed class LicencePendingReservationEntity
{
    public Guid LicencePendingReservationId { get; set; }

    public Guid LicenceSubjectId { get; set; }
    public LicenceSubjectEntity? LicenceSubject { get; set; }

    /// <summary>Exact signed licence transaction UUID (public licence reference / idempotency key).</summary>
    public Guid OriginatingTransactionId { get; set; }

    /// <summary>SHA-256 fingerprint of the exact canonical signed transaction bytes (lowercase hex).</summary>
    public string CanonicalPayloadFingerprintSha256 { get; set; } = string.Empty;

    /// <summary>Closed intent: baseline_free | confirmed_upgrade.</summary>
    public string TransitionIntent { get; set; } = string.Empty;

    /// <summary>Requested plan id (bounded FEAT-012 stable plan id).</summary>
    public string RequestedPlanId { get; set; } = string.Empty;

    /// <summary>Immutable catalogue version observed when the transition was presented.</summary>
    public string ObservedCatalogueVersion { get; set; } = string.Empty;

    /// <summary>Upgrade precondition: observed current licence transaction id (null for baseline).</summary>
    public Guid? ExpectedCurrentLicenceTransactionId { get; set; }

    /// <summary>Upgrade precondition: observed current plan id (null for baseline).</summary>
    public string? ExpectedCurrentPlanId { get; set; }

    /// <summary>Closed reservation lifecycle: pending | superseded | resolved.</summary>
    public string LifecycleStatus { get; set; } = LicencePersistenceVocabulary.ReservationLifecyclePending;

    /// <summary>Rank used for deterministic highest-valid-competition resolution (server-derived, never client).</summary>
    public int RequestedUpgradeRank { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}
