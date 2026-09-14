using System.Collections.Concurrent;
using HushNode.HushVoting.Licensing.Cache;

namespace HushNode.HushVoting.Licensing.Cache.Tests;

/// <summary>
/// Deterministic in-memory double of <see cref="IEntitlementProjectionStore"/> for orchestration
/// tests. It mirrors the revision-CAS/lease semantics of the Lua scripts (same decision table) so the
/// reader's behavior is exercised without a live Redis. The Lua scripts themselves are qualified by
/// the real-Redis TwinTests in Phase 7.
/// </summary>
public sealed class InMemoryEntitlementStore : IEntitlementProjectionStore
{
    private readonly LicenceCacheEnvelopeCodec _codec = new();
    private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (string Token, DateTime ExpiresUtc)> _leases = new(StringComparer.Ordinal);
    private readonly LicenceCacheOptions _options = new();

    public int ReadCalls => ReadCallsCount;
    private int ReadCallsCount;

    public IReadOnlyDictionary<string, string> Values => _values;

    public void Seed(string key, byte[] envelopeBytes, byte[] authKey)
    {
        var tag = _codec.ComputeAuthenticationTag(key, envelopeBytes, authKey);
        _values[key] = _codec.FormatRedisValue(envelopeBytes, tag);
    }

    public bool TryGetEnvelope(string key, byte[] authKey, out CachedEntitlementEnvelope? envelope)
    {
        envelope = null;
        if (!_values.TryGetValue(key, out var value))
        {
            return false;
        }

        if (!_codec.TrySplitRedisValue(value, _options.MaxEnvelopeBytes, out var bytes, out var tag, out _) ||
            !_codec.VerifyAuthentication(key, bytes, tag, authKey) ||
            !_codec.TryDeserialize(bytes, out envelope, out _))
        {
            return false;
        }

        return true;
    }

    public Task<StoreReadResult> ReadCurrentAsync(string fullKey, CancellationToken cancellationToken)
    {
        ReadCallsCount++;
        if (!_values.TryGetValue(fullKey, out var value))
        {
            return Task.FromResult(new StoreReadResult(false, Array.Empty<byte>(), Array.Empty<byte>(), false, null));
        }

        if (!_codec.TrySplitRedisValue(value, _options.MaxEnvelopeBytes, out var bytes, out var tag, out var reason))
        {
            return Task.FromResult(new StoreReadResult(true, Array.Empty<byte>(), Array.Empty<byte>(), false, reason));
        }

        return Task.FromResult(new StoreReadResult(true, bytes, tag, true, null));
    }

    public Task<StoreWriteResult> WriteAsync(
        string fullKey,
        byte[] canonicalEnvelopeBytes,
        long entitlementRevision,
        long redisTtlSeconds,
        byte[] valueAuthenticationKey,
        CancellationToken cancellationToken)
    {
        if (redisTtlSeconds <= 0)
        {
            return Task.FromResult(new StoreWriteResult(false, ProjectionWriteOutcome.StaleRevision));
        }

        var tag = _codec.ComputeAuthenticationTag(fullKey, canonicalEnvelopeBytes, valueAuthenticationKey);
        var incoming = _codec.FormatRedisValue(canonicalEnvelopeBytes, tag);

        if (!_values.TryGetValue(fullKey, out var existing))
        {
            _values[fullKey] = incoming;
            return Task.FromResult(new StoreWriteResult(true, ProjectionWriteOutcome.AcceptedNew));
        }

        var existingRevision = ExtractRevision(existing);
        if (existingRevision is null)
        {
            _values.TryRemove(fullKey, out _);
            _values[fullKey] = incoming;
            return Task.FromResult(new StoreWriteResult(true, ProjectionWriteOutcome.InvalidExistingRemoved));
        }

        if (entitlementRevision > existingRevision.Value)
        {
            _values[fullKey] = incoming;
            return Task.FromResult(new StoreWriteResult(true, ProjectionWriteOutcome.AcceptedHigherRevision));
        }

        if (entitlementRevision == existingRevision.Value)
        {
            return string.Equals(existing, incoming, StringComparison.Ordinal)
                ? Task.FromResult(new StoreWriteResult(true, ProjectionWriteOutcome.IdempotentEqual))
                : Task.FromResult(new StoreWriteResult(true, ProjectionWriteOutcome.SameRevisionDivergence));
        }

        return Task.FromResult(new StoreWriteResult(true, ProjectionWriteOutcome.StaleRevision));
    }

    public Task<StoreWriteResult> RemoveAsync(string fullKey, CancellationToken cancellationToken)
    {
        _values.TryRemove(fullKey, out _);
        return Task.FromResult(new StoreWriteResult(true, ProjectionWriteOutcome.StaleRevision));
    }

    public Task<bool> TryAcquireLeaseAsync(string leaseKey, string ownershipToken, int leaseSeconds, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (_leases.TryGetValue(leaseKey, out var existing) && existing.ExpiresUtc > now)
        {
            return Task.FromResult(false);
        }

        _leases[leaseKey] = (ownershipToken, now.AddSeconds(leaseSeconds));
        return Task.FromResult(true);
    }

    public Task<bool> TryReleaseLeaseAsync(string leaseKey, string ownershipToken, CancellationToken cancellationToken)
    {
        if (_leases.TryGetValue(leaseKey, out var existing) &&
            string.Equals(existing.Token, ownershipToken, StringComparison.Ordinal))
        {
            _leases.TryRemove(leaseKey, out _);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    private static long? ExtractRevision(string value)
    {
        var lastNewline = value.LastIndexOf('\n');
        if (lastNewline <= 0)
        {
            return null;
        }

        var json = value[..lastNewline];
        var marker = "\"entitlementRevision\":";
        var idx = json.LastIndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
        {
            return null;
        }

        var rest = json[(idx + marker.Length)..];
        var end = 0;
        while (end < rest.Length && char.IsDigit(rest[end]))
        {
            end++;
        }

        return end == 0 ? null : long.Parse(rest[..end]);
    }
}
