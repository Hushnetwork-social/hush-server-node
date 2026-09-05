namespace HushNode.HushVoting.Licensing.Cache;

/// <summary>
/// Non-secret configuration bounds for the HushVoting licence display cache. Secret HMAC master-key
/// material is never part of this type (it is supplied through environment/deployment secrets and
/// validated separately by the key ring, Phase 2 Task 2.5).
///
/// <para>Fixed v1 values are frozen by the FEAT-014 specification: seven-day maximum TTL, 0-10%
/// deterministic subject jitter, fourteen-day previous-key overlap, five-second fill lease, 750 ms
/// waiter budget, three-failure/30-second circuit, 16 KiB envelope, thirty-day delivered outbox
/// retention, and fixed outbox health warning/critical thresholds.</para>
/// </summary>
public sealed class LicenceCacheOptions
{
    public const int DefaultMaxTtlDays = 7;
    public const int MaxJitterPercent = 10;
    public const int DefaultPreviousKeyOverlapMaxDays = 14;
    public const int DefaultFillLeaseSeconds = 5;
    public const int DefaultWaiterPollBudgetMs = 750;
    public const int DefaultCircuitOpenFailureCount = 3;
    public const int DefaultCircuitOpenSeconds = 30;
    public const int DefaultMaxEnvelopeBytes = 16 * 1024;
    public const int DefaultDeliveredRetentionDays = 30;
    public const int DefaultOutboxClaimBatchSize = 100;

    public bool Enabled { get; init; } = true;

    /// <summary>Absolute, non-sliding maximum Redis TTL for licence projections in days.</summary>
    public int MaxTtlDays { get; init; } = DefaultMaxTtlDays;

    /// <summary>Deterministic per-subject TTL reduction ceiling as a percentage (0..10).</summary>
    public int MaxTtlJitterPercent { get; init; } = MaxJitterPercent;

    /// <summary>Maximum configured overlap for the previous key in days (rotation guard).</summary>
    public int PreviousKeyOverlapMaxDays { get; init; } = DefaultPreviousKeyOverlapMaxDays;

    /// <summary>Distributed fill-lease lifetime in seconds (cryptographically random ownership token).</summary>
    public int FillLeaseSeconds { get; init; } = DefaultFillLeaseSeconds;

    /// <summary>Bounded time a non-owner waiter polls Redis for a filled value before calling FEAT-013.</summary>
    public int WaiterPollBudgetMs { get; init; } = DefaultWaiterPollBudgetMs;

    /// <summary>Consecutive Redis connection/timeout failures that open the licence-cache circuit.</summary>
    public int CircuitOpenFailureCount { get; init; } = DefaultCircuitOpenFailureCount;

    /// <summary>Seconds the licence-cache circuit stays open before one half-open probe is allowed.</summary>
    public int CircuitOpenSeconds { get; init; } = DefaultCircuitOpenSeconds;

    /// <summary>Hard bound for one authenticated canonical envelope in bytes.</summary>
    public int MaxEnvelopeBytes { get; init; } = DefaultMaxEnvelopeBytes;

    /// <summary>Delivered outbox rows are retained for this many days before bounded cleanup.</summary>
    public int DeliveredRetentionDays { get; init; } = DefaultDeliveredRetentionDays;

    /// <summary>Maximum outbox rows claimed per dispatcher batch.</summary>
    public int OutboxClaimBatchSize { get; init; } = DefaultOutboxClaimBatchSize;

    /// <summary>Outbox health warning oldest-pending threshold.</summary>
    public TimeSpan OutboxHealthWarningOldestAge { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Outbox health warning pending-depth threshold.</summary>
    public int OutboxHealthWarningDepth { get; init; } = 1_000;

    /// <summary>Outbox health critical oldest-pending threshold.</summary>
    public TimeSpan OutboxHealthCriticalOldestAge { get; init; } = TimeSpan.FromHours(1);

    /// <summary>Outbox health critical pending-depth threshold.</summary>
    public int OutboxHealthCriticalDepth { get; init; } = 10_000;

    /// <summary>Key ID length bounds (stable non-secret identifiers).</summary>
    public int MinKeyIdCharacters { get; init; } = 1;

    public int MaxKeyIdCharacters { get; init; } = 64;

    /// <summary>Minimum decoded master-key entropy in bytes (&gt;= 256 bits).</summary>
    public int MinMasterKeyBytes { get; init; } = 32;

    /// <summary>
    /// Validates bounded options and returns the first stable error code, or <c>null</c> when valid.
    /// Validation never reads or logs secret material.
    /// </summary>
    public string? Validate()
    {
        if (MaxTtlDays is < 1 or > 30)
        {
            return LicenceCacheOptionErrorCodes.InvalidMaxTtl;
        }

        if (MaxTtlJitterPercent is < 0 or > MaxJitterPercent)
        {
            return LicenceCacheOptionErrorCodes.InvalidJitter;
        }

        if (PreviousKeyOverlapMaxDays is < 0 or > 30)
        {
            return LicenceCacheOptionErrorCodes.InvalidRotationStartedAt;
        }

        if (FillLeaseSeconds is < 1 or > 60)
        {
            return LicenceCacheOptionErrorCodes.InvalidLeaseSeconds;
        }

        if (WaiterPollBudgetMs is < 0 or > 5_000)
        {
            return LicenceCacheOptionErrorCodes.InvalidWaiterBudget;
        }

        if (CircuitOpenFailureCount is < 1 or > 20)
        {
            return LicenceCacheOptionErrorCodes.InvalidCircuitThreshold;
        }

        if (CircuitOpenSeconds is < 1 or > 300)
        {
            return LicenceCacheOptionErrorCodes.InvalidCircuitOpenSeconds;
        }

        if (MaxEnvelopeBytes is < 512 or > 64 * 1024)
        {
            return LicenceCacheOptionErrorCodes.InvalidMaxEnvelopeBytes;
        }

        if (DeliveredRetentionDays is < 1 or > 365)
        {
            return LicenceCacheOptionErrorCodes.InvalidRetentionDays;
        }

        if (OutboxClaimBatchSize is < 1 or > 1_000)
        {
            return LicenceCacheOptionErrorCodes.InvalidBatchSize;
        }

        if (OutboxHealthWarningOldestAge <= TimeSpan.Zero ||
            OutboxHealthCriticalOldestAge <= OutboxHealthWarningOldestAge)
        {
            return LicenceCacheOptionErrorCodes.InvalidHealthThreshold;
        }

        if (OutboxHealthWarningDepth is < 1 || OutboxHealthCriticalDepth <= OutboxHealthWarningDepth)
        {
            return LicenceCacheOptionErrorCodes.InvalidHealthThreshold;
        }

        if (MinKeyIdCharacters is < 1 || MaxKeyIdCharacters < MinKeyIdCharacters ||
            MaxKeyIdCharacters > 128)
        {
            return LicenceCacheOptionErrorCodes.InvalidKeyIdLength;
        }

        if (MinMasterKeyBytes < 32)
        {
            return LicenceCacheOptionErrorCodes.WeakCurrentKeyEntropy;
        }

        return null;
    }
}
