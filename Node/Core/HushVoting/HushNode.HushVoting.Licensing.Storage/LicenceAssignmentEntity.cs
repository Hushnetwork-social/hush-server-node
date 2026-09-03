namespace HushNode.HushVoting.Licensing.Storage;

/// <summary>
/// One licence assignment. Rows are never deleted. The operative snapshot pinned at
/// assignment time stays authoritative for the assignment's lifetime.
/// </summary>
public sealed class LicenceAssignmentEntity
{
    public Guid LicenceAssignmentId { get; set; }

    public Guid LicenceSubjectId { get; set; }
    public LicenceSubjectEntity? LicenceSubject { get; set; }

    /// <summary>Stable FEAT-012 plan id (e.g. hushvoting.direct.free).</summary>
    public string PlanId { get; set; } = string.Empty;

    public string AssignedCatalogueVersion { get; set; } = string.Empty;
    public string AssignedCatalogueDigestSha256 { get; set; } = string.Empty;

    /// <summary>Closed lifecycle: active | superseded | expired.</summary>
    public string LifecycleStatus { get; set; } = LicencePersistenceVocabulary.LifecycleActive;

    /// <summary>Closed source: default_free | migration_lazy_default | automatic_upgrade | automatic_expiry.</summary>
    public string Source { get; set; } = string.Empty;

    public DateTime EffectiveFromUtc { get; set; }

    /// <summary>Upper-exclusive expiry for annual assignments; null for perpetual Direct Free.</summary>
    public DateTime? ExpiresAtUtc { get; set; }

    public DateTime? LifecycleChangedAtUtc { get; set; }
    public string? LifecycleReason { get; set; }

    // ---- Immutable assignment-time operative snapshot (pinned; never silently changed) ----
    public string PlanFamily { get; set; } = string.Empty;
    public int UpgradeRank { get; set; }
    public int? EligibleVoterCap { get; set; }
    public bool UnlimitedElectionPolicy { get; set; }
    public string TermKind { get; set; } = LicencePersistenceVocabulary.TermPerpetual;
    public int TermYears { get; set; }
    public string[] AllowedGovernanceOptionIds { get; set; } = Array.Empty<string>();

    /// <summary>Privacy-safe creation correlation (e.g. the provisioning/activation operation id); no raw identity.</summary>
    public string? CreationCorrelationId { get; set; }

    /// <summary>Activation operation that produced this assignment, when applicable.</summary>
    public Guid? CreatedByOperationId { get; set; }
    public LicenceActivationOperationEntity? CreatedByOperation { get; set; }
}
