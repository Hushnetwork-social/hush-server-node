namespace HushNode.HushVoting.Licensing.Cache;

/// <summary>
/// Stable bounded option-error codes for licence-cache configuration validation. Codes are safe for
/// diagnostics/logs: they never disclose secret values.
/// </summary>
public static class LicenceCacheOptionErrorCodes
{
    public const string InvalidEnabledFlag = "cache_options_invalid_enabled_flag";
    public const string InvalidMaxTtl = "cache_options_invalid_max_ttl";
    public const string InvalidJitter = "cache_options_invalid_jitter_percent";
    public const string InvalidKeyId = "cache_options_invalid_key_id";
    public const string InvalidKeyIdLength = "cache_options_invalid_key_id_length";
    public const string DuplicateKeyId = "cache_options_duplicate_key_id";
    public const string WeakCurrentKeyEntropy = "cache_options_current_key_entropy_too_low";
    public const string WeakPreviousKeyEntropy = "cache_options_previous_key_entropy_too_low";
    public const string MissingCurrentKey = "cache_options_missing_current_key";
    public const string InvalidPreviousKey = "cache_options_invalid_previous_key";
    public const string OverlappingPreviousKey = "cache_options_previous_key_must_precede_current";
    public const string OverlapBeyondLimit = "cache_options_key_overlap_exceeds_limit";
    public const string InvalidRotationStartedAt = "cache_options_invalid_rotation_started_at";
    public const string InvalidLeaseSeconds = "cache_options_invalid_lease_seconds";
    public const string InvalidWaiterBudget = "cache_options_invalid_waiter_budget";
    public const string InvalidCircuitThreshold = "cache_options_invalid_circuit_threshold";
    public const string InvalidCircuitOpenSeconds = "cache_options_invalid_circuit_open_seconds";
    public const string InvalidMaxEnvelopeBytes = "cache_options_invalid_max_envelope_bytes";
    public const string InvalidRetentionDays = "cache_options_invalid_retention_days";
    public const string InvalidBatchSize = "cache_options_invalid_batch_size";
    public const string InvalidHealthThreshold = "cache_options_invalid_health_threshold";
    public const string InvalidInstancePrefix = "cache_options_invalid_instance_prefix";
    public const string InvalidJitterDeterministicSeed = "cache_options_invalid_jitter_seed";
}
