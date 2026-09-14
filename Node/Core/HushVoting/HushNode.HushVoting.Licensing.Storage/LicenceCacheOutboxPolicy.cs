namespace HushNode.HushVoting.Licensing.Storage;

/// <summary>
/// Optional FEAT-014 cache-outbox contribution accepted by FEAT-013 mutation entry points (Phase 6).
/// When enabled, the coordinator writes exactly one privacy-safe <c>LicenceCacheOutbox</c> row inside
/// the same PostgreSQL transaction and committed subject revision for each cache-relevant state
/// change (provisioning, activation, annual expiry to default). The optional committed publisher runs
/// only after a successful authoritative commit or reconciliation and is best-effort: it never
/// changes, reverses, or hides the committed FEAT-013 result.
/// </summary>
public sealed class LicenceCacheOutboxPolicy
{
    public static LicenceCacheOutboxPolicy Disabled { get; } = new(false, null);

    /// <summary>True when cache outbox rows must be written for cache-relevant state changes.</summary>
    public bool Enabled { get; }

    /// <summary>
    /// Best-effort immediate post-commit publisher (Redis projection write). The signature uses only
    /// authoritative FEAT-013 types so the Storage module never references the Cache module.
    /// </summary>
    public Func<AuthenticatedIdentitySubject, EffectiveLicenceEntitlement, CancellationToken, Task>?
        CommittedPublisher { get; }

    public LicenceCacheOutboxPolicy(
        bool enabled,
        Func<AuthenticatedIdentitySubject, EffectiveLicenceEntitlement, CancellationToken, Task>?
            committedPublisher)
    {
        Enabled = enabled;
        CommittedPublisher = committedPublisher;
    }
}
