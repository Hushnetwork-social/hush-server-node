using System.Collections.Concurrent;

namespace HushNode.HushVoting.Licensing.Cache;

/// <summary>
/// Bounded privacy-safe cache telemetry. Labels come from closed vocabularies only; no raw address,
/// subject digest, Redis key, database id, plan history, or unbounded exception value is ever stored.
/// </summary>
public sealed class LicenceCacheTelemetry
{
    private readonly ConcurrentDictionary<string, long> _counters = new(StringComparer.Ordinal);

    public void Count(string closedLabel)
    {
        if (string.IsNullOrWhiteSpace(closedLabel) || closedLabel.Length > 64)
        {
            return;
        }

        _counters.AddOrUpdate(closedLabel, 1, static (_, existing) => existing + 1);
    }

    public long Get(string closedLabel) => _counters.TryGetValue(closedLabel, out var value) ? value : 0;

    /// <summary>Immutable snapshot of all counters (diagnostics/tests only).</summary>
    public IReadOnlyDictionary<string, long> Snapshot() =>
        _counters.ToArray().ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
}

/// <summary>Closed v1 outbox health state.</summary>
public enum LicenceCacheHealthState
{
    Healthy,
    Warning,
    Critical,
}

/// <summary>Health evaluation over the fixed v1 thresholds; cache degradation never fails node readiness.</summary>
public static class LicenceCacheHealth
{
    public static LicenceCacheHealthState EvaluateOutbox(
        LicenceCacheOptions options,
        long pendingDepth,
        TimeSpan oldestPendingAge) =>
        // Fixed v1 thresholds are strict ("exceeds five minutes / exceeds 1 000 rows / exceeds one
        // hour / exceeds 10 000 rows").
        pendingDepth > options.OutboxHealthCriticalDepth ||
        oldestPendingAge > options.OutboxHealthCriticalOldestAge
            ? LicenceCacheHealthState.Critical
            : pendingDepth > options.OutboxHealthWarningDepth ||
              oldestPendingAge > options.OutboxHealthWarningOldestAge
                ? LicenceCacheHealthState.Warning
                : LicenceCacheHealthState.Healthy;
}
