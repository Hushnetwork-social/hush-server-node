namespace HushNode.HushVoting.Licensing.Cache;

/// <summary>
/// Closed v1 cache runtime mode reported by the node's licence-cache host composition (Phase 6).
/// Disabled and degraded modes never fail node readiness; invalid enabled security configuration
/// fails licensing-cache readiness only (FEAT-013 authority remains available).
/// </summary>
public enum LicenceCacheRuntimeMode
{
    /// <summary>The licence cache is intentionally disabled; reads resolve through FEAT-013 directly.</summary>
    Disabled,

    /// <summary>Cache enabled with a validated key ring and an operational Redis capability.</summary>
    Ready,

    /// <summary>Cache enabled but Redis is transiently unavailable/degraded; reads fall back to FEAT-013.</summary>
    Degraded,

    /// <summary>Cache enabled but the security configuration (options/keys) is invalid; readiness failed.</summary>
    ConfigurationFailed,
}

/// <summary>
/// Result of one cache readiness evaluation: the runtime mode, whether the overall node remains
/// available (only an invalid enabled security configuration makes the node unavailable), and a
/// stable bounded diagnostic code that never contains secret material.
/// </summary>
public sealed record LicenceCacheReadinessResult(
    LicenceCacheRuntimeMode Mode,
    bool NodeAvailable,
    string? StableCode)
{
    public static LicenceCacheReadinessResult Ok(LicenceCacheRuntimeMode mode) =>
        new(mode, NodeAvailable: mode != LicenceCacheRuntimeMode.ConfigurationFailed, StableCode: null);

    public static LicenceCacheReadinessResult Failed(string stableCode) =>
        new(LicenceCacheRuntimeMode.ConfigurationFailed, NodeAvailable: false, StableCode: stableCode);
}

/// <summary>
/// Deterministic readiness evaluation for the HushVoting licence display cache. Node-level
/// availability is independent from cache mode: only an invalid enabled security configuration
/// fails cache readiness; disabled and transient Redis-degraded states keep the node available.
/// </summary>
public static class LicenceCacheRuntimeStatus
{
    /// <summary>
    /// Evaluates the runtime mode from the explicit configuration and capability inputs.
    /// Pure and deterministic so host tests and readiness probes share one decision table.
    /// </summary>
    /// <param name="enabled">Licence-cache enabled snapshot from options.</param>
    /// <param name="securityConfigurationError">
    /// Stable error code when the enabled cache options/key ring are invalid; null when valid or disabled.
    /// </param>
    /// <param name="redisUsable">Whether the shared Redis capability is currently usable (false = degraded).</param>
    public static LicenceCacheReadinessResult Evaluate(
        bool enabled,
        string? securityConfigurationError,
        bool redisUsable)
    {
        if (!enabled)
        {
            return LicenceCacheReadinessResult.Ok(LicenceCacheRuntimeMode.Disabled);
        }

        if (securityConfigurationError is not null)
        {
            return LicenceCacheReadinessResult.Failed(securityConfigurationError);
        }

        return LicenceCacheReadinessResult.Ok(
            redisUsable ? LicenceCacheRuntimeMode.Ready : LicenceCacheRuntimeMode.Degraded);
    }
}
