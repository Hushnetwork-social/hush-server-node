using System.Security.Cryptography;
using System.Text;
using StackExchange.Redis;

namespace HushNode.HushVoting.Licensing.Cache;

/// <summary>
/// Outcome of one revision-aware projection write (closed vocabulary).
/// </summary>
public enum ProjectionWriteOutcome
{
    /// <summary>No existing value: fresh valid projection accepted.</summary>
    AcceptedNew = 1,

    /// <summary>Higher entitlement revision replaced the existing projection atomically.</summary>
    AcceptedHigherRevision = 2,

    /// <summary>Same revision with byte-identical authenticated payload: idempotent no-op; TTL is not extended.</summary>
    IdempotentEqual = 3,

    /// <summary>Same revision with different protected content: rejected as divergent (invariant alert).</summary>
    SameRevisionDivergence = 4,

    /// <summary>Lower revision: rejected as stale; the newer projection is preserved.</summary>
    StaleRevision = 5,

    /// <summary>Existing value was invalid; the store removed it and a fresh authoritative write must follow.</summary>
    InvalidExistingRemoved = 6,
}

/// <summary>
/// Strict Redis projection store: bounded reads with validation/removal, current-to-previous rotation
/// lookup, atomic monotonic revision-aware replacement with conservative TTL, and token-owned
/// distributed fill leases. All commands are bounded and cancellable; no command enumerates keys.
/// The lease and the projection share one Redis hash tag per subject so cluster slots are preserved.
/// </summary>
public interface IEntitlementProjectionStore
{
    Task<StoreReadResult> ReadCurrentAsync(string fullKey, CancellationToken cancellationToken);

    Task<StoreWriteResult> WriteAsync(
        string fullKey,
        byte[] canonicalEnvelopeBytes,
        long entitlementRevision,
        long redisTtlSeconds,
        byte[] valueAuthenticationKey,
        CancellationToken cancellationToken);

    Task<StoreWriteResult> RemoveAsync(string fullKey, CancellationToken cancellationToken);

    Task<bool> TryAcquireLeaseAsync(
        string leaseKey,
        string ownershipToken,
        int leaseSeconds,
        CancellationToken cancellationToken);

    Task<bool> TryReleaseLeaseAsync(
        string leaseKey,
        string ownershipToken,
        CancellationToken cancellationToken);
}

/// <summary>Result of a bounded projection read.</summary>
public sealed record StoreReadResult(
    bool Found,
    byte[] CanonicalEnvelopeBytes,
    byte[] TagBytes,
    bool Valid,
    string? StableRejectReason);

/// <summary>Result of a write/remove operation.</summary>
public sealed record StoreWriteResult(bool Success, ProjectionWriteOutcome Outcome);

/// <summary>
/// Default store implementation over the shared Redis <see cref="IDatabase"/>. The Lua CAS script
/// compares the numeric entitlement revision embedded in the canonical envelope, so equal-identical
/// payloads never extend TTL and stale/divergent content can never replace newer projections.
/// </summary>
public sealed class RedisEntitlementProjectionStore : IEntitlementProjectionStore
{
    private const string RevisionPattern = "\"entitlementRevision\":(%d+)";

    private const string CasScript = """
        local existing = redis.call('GET', KEYS[1])
        if not existing then
            redis.call('SET', KEYS[1], ARGV[1], 'EX', ARGV[2])
            return 1
        end
        local eRev = tonumber(string.match(existing, ARGV[4]))
        if eRev == nil then
            redis.call('DEL', KEYS[1])
            return 6
        end
        local iRev = tonumber(ARGV[3])
        if iRev > eRev then
            redis.call('SET', KEYS[1], ARGV[1], 'EX', ARGV[2])
            return 2
        end
        if iRev == eRev then
            if existing == ARGV[1] then
                return 3
            end
            return 4
        end
        return 5
        """;

    private const string LeaseAcquireScript = """
        local ok = redis.call('SET', KEYS[1], ARGV[1], 'PX', ARGV[2], 'NX')
        if ok then return 1 end
        return 0
        """;

    private const string LeaseReleaseScript = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            redis.call('DEL', KEYS[1])
            return 1
        end
        return 0
        """;

    private readonly IDatabase _database;
    private readonly LicenceCacheOptions _options;
    private readonly LicenceCacheEnvelopeCodec _codec;

    public RedisEntitlementProjectionStore(
        IDatabase database,
        LicenceCacheOptions options,
        LicenceCacheEnvelopeCodec codec)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
    }

    public async Task<StoreReadResult> ReadCurrentAsync(string fullKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fullKey);
        cancellationToken.ThrowIfCancellationRequested();

        var redisValue = await _database.StringGetAsync(fullKey).ConfigureAwait(false);
        if (redisValue.IsNull)
        {
            return new StoreReadResult(false, Array.Empty<byte>(), Array.Empty<byte>(), false, null);
        }

        if (!_codec.TrySplitRedisValue(
                redisValue.ToString(),
                _options.MaxEnvelopeBytes,
                out var envelopeBytes,
                out var tagBytes,
                out var reason))
        {
            return new StoreReadResult(true, Array.Empty<byte>(), Array.Empty<byte>(), false, reason);
        }

        return new StoreReadResult(true, envelopeBytes, tagBytes, true, null);
    }

    public async Task<StoreWriteResult> WriteAsync(
        string fullKey,
        byte[] canonicalEnvelopeBytes,
        long entitlementRevision,
        long redisTtlSeconds,
        byte[] valueAuthenticationKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fullKey);
        ArgumentNullException.ThrowIfNull(canonicalEnvelopeBytes);
        ArgumentNullException.ThrowIfNull(valueAuthenticationKey);
        cancellationToken.ThrowIfCancellationRequested();

        if (redisTtlSeconds <= 0)
        {
            return new StoreWriteResult(false, ProjectionWriteOutcome.StaleRevision);
        }

        var tag = _codec.ComputeAuthenticationTag(fullKey, canonicalEnvelopeBytes, valueAuthenticationKey);
        var value = _codec.FormatRedisValue(canonicalEnvelopeBytes, tag);

        var result = (long)await _database.ScriptEvaluateAsync(
            CasScript,
            new RedisKey[] { fullKey },
            new RedisValue[]
            {
                value,
                redisTtlSeconds,
                entitlementRevision,
                RevisionPattern,
            }).ConfigureAwait(false);

        if ((int)result == (int)ProjectionWriteOutcome.InvalidExistingRemoved)
        {
            // Existing value was invalid and has been removed; retry once with the fresh authoritative
            // projection (only freshly authoritative content may replace an invalid value).
            result = (long)await _database.ScriptEvaluateAsync(
                CasScript,
                new RedisKey[] { fullKey },
                new RedisValue[]
                {
                    value,
                    redisTtlSeconds,
                    entitlementRevision,
                    RevisionPattern,
                }).ConfigureAwait(false);
        }

        return new StoreWriteResult(true, (ProjectionWriteOutcome)(int)result);
    }

    public async Task<StoreWriteResult> RemoveAsync(string fullKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fullKey);
        cancellationToken.ThrowIfCancellationRequested();
        await _database.KeyDeleteAsync(fullKey).ConfigureAwait(false);
        return new StoreWriteResult(true, ProjectionWriteOutcome.StaleRevision);
    }

    public async Task<bool> TryAcquireLeaseAsync(
        string leaseKey,
        string ownershipToken,
        int leaseSeconds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = (long)await _database.ScriptEvaluateAsync(
            LeaseAcquireScript,
            new RedisKey[] { leaseKey },
            new RedisValue[] { ownershipToken, leaseSeconds * 1000L }).ConfigureAwait(false);
        return result == 1;
    }

    public async Task<bool> TryReleaseLeaseAsync(
        string leaseKey,
        string ownershipToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = (long)await _database.ScriptEvaluateAsync(
            LeaseReleaseScript,
            new RedisKey[] { leaseKey },
            new RedisValue[] { ownershipToken }).ConfigureAwait(false);
        return result == 1;
    }

    /// <summary>Generates a cryptographically random lease ownership token (never logged).</summary>
    public static string NewOwnershipToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
}
