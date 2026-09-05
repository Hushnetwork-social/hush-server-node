namespace HushNode.HushVoting.Licensing.Cache;

/// <summary>
/// Non-authoritative, client-safe projection of one subject's effective entitlement served by the
/// display cache. It deliberately contains no internal database identifiers, no source/provenance
/// fields, no catalogue digest, no key material, no authentication metadata, no write time, and no
/// outbox state.
///
/// <para>This type is a display/read optimization only. It cannot implement, inherit from, or
/// implicitly convert to FEAT-013's authoritative <c>EffectiveLicenceEntitlement</c> or any
/// activation/enforcement authorization type. Activation and enforcement must depend only on the
/// authoritative FEAT-013 service.</para>
/// </summary>
public sealed record CachedEntitlementProjection
{
    public const int MaxAllowedGovernanceOptionIds = 64;

    public CachedEntitlementProjection(
        string planId,
        string planFamily,
        int upgradeRank,
        int? eligibleVoterCap,
        bool unlimitedElectionPolicy,
        string termKind,
        int termYears,
        IReadOnlyList<string> allowedGovernanceOptionIds,
        DateTime? expiresAtUtc,
        long entitlementRevision)
    {
        if (string.IsNullOrWhiteSpace(planId))
        {
            throw new ArgumentException("Plan id is required.", nameof(planId));
        }

        if (string.IsNullOrWhiteSpace(planFamily))
        {
            throw new ArgumentException("Plan family is required.", nameof(planFamily));
        }

        if (upgradeRank < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(upgradeRank));
        }

        if (eligibleVoterCap is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(eligibleVoterCap));
        }

        if (string.IsNullOrWhiteSpace(termKind))
        {
            throw new ArgumentException("Term kind is required.", nameof(termKind));
        }

        if (termYears <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(termYears));
        }

        if (entitlementRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entitlementRevision));
        }

        ArgumentNullException.ThrowIfNull(allowedGovernanceOptionIds);
        if (allowedGovernanceOptionIds.Count > MaxAllowedGovernanceOptionIds)
        {
            throw new ArgumentException(
                $"Allowed governance option ids exceed the bound of {MaxAllowedGovernanceOptionIds}.",
                nameof(allowedGovernanceOptionIds));
        }

        PlanId = planId;
        PlanFamily = planFamily;
        UpgradeRank = upgradeRank;
        EligibleVoterCap = eligibleVoterCap;
        UnlimitedElectionPolicy = unlimitedElectionPolicy;
        TermKind = termKind;
        TermYears = termYears;
        AllowedGovernanceOptionIds = allowedGovernanceOptionIds;
        ExpiresAtUtc = expiresAtUtc;
        EntitlementRevision = entitlementRevision;
    }

    /// <summary>FEAT-012 stable plan id (for example <c>hushvoting.direct.free</c>).</summary>
    public string PlanId { get; }

    /// <summary>FEAT-012 plan family (for example <c>Direct</c> or <c>Veritas</c>).</summary>
    public string PlanFamily { get; }

    /// <summary>Monotonic upgrade rank used by catalogue transition decisions.</summary>
    public int UpgradeRank { get; }

    /// <summary>Eligible voter cap when the plan is capped; <c>null</c> when unlimited or N/A.</summary>
    public int? EligibleVoterCap { get; }

    /// <summary>Whether the plan permits unlimited elections.</summary>
    public bool UnlimitedElectionPolicy { get; }

    /// <summary>Term kind (for example <c>annual</c>); empty policy only for Direct Free semantics.</summary>
    public string TermKind { get; }

    /// <summary>Term length in years.</summary>
    public int TermYears { get; }

    /// <summary>Client-safe governance option ids the plan allows (closed FEAT-012 ids).</summary>
    public IReadOnlyList<string> AllowedGovernanceOptionIds { get; }

    /// <summary>Assignment upper-exclusive expiry when the plan is time bound; <c>null</c> otherwise.</summary>
    public DateTime? ExpiresAtUtc { get; }

    /// <summary>Monotonic FEAT-013 entitlement revision the projection was captured at.</summary>
    public long EntitlementRevision { get; }
}
