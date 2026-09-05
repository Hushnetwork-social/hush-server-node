using System.Collections.Concurrent;

namespace HushNode.HushVoting.Licensing.Cache;

/// <summary>
/// Same-key single-flight coordination: concurrent misses for one cache key coalesce onto one
/// in-flight fill while waiters poll Redis within a bounded budget and otherwise fall back to the
/// authoritative resolver. There is no cross-subject blocking and no in-process licence-value cache
/// beyond this task coalescing.
/// </summary>
public sealed class LicenceCacheSingleFlight
{
    private readonly ConcurrentDictionary<string, Entry> _inflight = new();

    private sealed record Entry(TaskCompletionSource<bool> Completion);

    /// <summary>
    /// Attempts to become the fill owner for <paramref name="flightKey"/>. Only one caller per key
    /// becomes the owner; all others receive a wait task that completes when the owner finishes
    /// (success or failure) so they can re-check Redis.
    /// </summary>
    public bool TryBecomeOwner(string flightKey, out Task ownerFinished)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entry = new Entry(tcs);
        if (_inflight.TryAdd(flightKey, entry))
        {
            ownerFinished = tcs.Task;
            return true;
        }

        ownerFinished = _inflight.TryGetValue(flightKey, out var existing)
            ? existing.Completion.Task
            : Task.CompletedTask;
        return false;
    }

    /// <summary>Signals waiters that the owner finished, then removes the slot.</summary>
    public void FinishOwner(string flightKey)
    {
        if (_inflight.TryRemove(flightKey, out var entry))
        {
            entry.Completion.TrySetResult(true);
        }
    }

    /// <summary>Allows tests to observe that no in-flight slot remains (no task leak).</summary>
    public int InFlightCount => _inflight.Count;
}
