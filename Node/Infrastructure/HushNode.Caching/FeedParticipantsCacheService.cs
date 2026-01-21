using System.Text.Json;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace HushNode.Caching;

/// <summary>
/// Service for caching feed participants and key generations in Redis (FEAT-050).
/// Part A: Feed participants stored as Redis SET for fast membership lookups.
/// Part B: Key generations stored as JSON blob for client decryption.
/// Implements cache-aside pattern with sliding TTL expiration.
/// </summary>
public class FeedParticipantsCacheService : IFeedParticipantsCacheService
{
    private readonly IDatabase _database;
    private readonly string _keyPrefix;
    private readonly ILogger<FeedParticipantsCacheService> _logger;

    // Cache metrics (simple counters for observability)
    private long _cacheHits;
    private long _cacheMisses;
    private long _writeOperations;
    private long _writeErrors;
    private long _readErrors;

    /// <summary>
    /// Gets the total number of cache hits.
    /// </summary>
    public long CacheHits => Interlocked.Read(ref _cacheHits);

    /// <summary>
    /// Gets the total number of cache misses.
    /// </summary>
    public long CacheMisses => Interlocked.Read(ref _cacheMisses);

    /// <summary>
    /// Gets the total number of write operations.
    /// </summary>
    public long WriteOperations => Interlocked.Read(ref _writeOperations);

    /// <summary>
    /// Gets the total number of write errors.
    /// </summary>
    public long WriteErrors => Interlocked.Read(ref _writeErrors);

    /// <summary>
    /// Gets the total number of read errors.
    /// </summary>
    public long ReadErrors => Interlocked.Read(ref _readErrors);

    /// <summary>
    /// Creates a new instance of FeedParticipantsCacheService.
    /// </summary>
    /// <param name="connectionMultiplexer">Redis connection multiplexer.</param>
    /// <param name="keyPrefix">Prefix for all Redis keys (e.g., "HushFeeds:").</param>
    /// <param name="logger">Logger instance.</param>
    public FeedParticipantsCacheService(
        IConnectionMultiplexer connectionMultiplexer,
        string keyPrefix,
        ILogger<FeedParticipantsCacheService> logger)
    {
        _database = connectionMultiplexer.GetDatabase();
        _keyPrefix = keyPrefix;
        _logger = logger;
    }

    #region Part A: Feed Participants (Redis SET)

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>?> GetParticipantsAsync(FeedId feedId)
    {
        var key = GetParticipantsRedisKey(feedId);

        try
        {
            // Check if key exists (cache miss detection)
            if (!await _database.KeyExistsAsync(key))
            {
                Interlocked.Increment(ref _cacheMisses);
                _logger.LogDebug("Cache miss for feed participants {FeedId}", feedId.Value);
                return null;
            }

            // Get all participant addresses from SET (SMEMBERS)
            var members = await _database.SetMembersAsync(key);

            // Refresh TTL on cache hit (sliding expiration)
            await _database.KeyExpireAsync(key, FeedParticipantsCacheConstants.CacheTtl);

            if (members.Length == 0)
            {
                Interlocked.Increment(ref _cacheHits);
                _logger.LogDebug("Cache hit for feed {FeedId} - empty participant set", feedId.Value);
                return Array.Empty<string>();
            }

            // Convert RedisValue[] to string[]
            var participants = new List<string>(members.Length);
            foreach (var member in members)
            {
                var address = member.ToString();
                if (!string.IsNullOrEmpty(address))
                {
                    participants.Add(address);
                }
            }

            Interlocked.Increment(ref _cacheHits);
            _logger.LogDebug(
                "Cache hit for feed {FeedId} - {ParticipantCount} participants",
                feedId.Value,
                participants.Count);

            return participants;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _readErrors);
            _logger.LogWarning(
                ex,
                "Failed to read participants from cache for feed {FeedId}. Returning null (cache miss).",
                feedId.Value);
            // Return null on error - caller should fall back to database
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetParticipantsAsync(FeedId feedId, IEnumerable<string> participantAddresses)
    {
        var participantList = participantAddresses.ToList();
        var key = GetParticipantsRedisKey(feedId);

        try
        {
            if (participantList.Count == 0)
            {
                // For empty participant list, still create the cache entry
                // This distinguishes "no participants" from "cache miss"
                var transaction = _database.CreateTransaction();
                _ = transaction.KeyDeleteAsync(key);
                _ = transaction.KeyExpireAsync(key, FeedParticipantsCacheConstants.CacheTtl);
                await transaction.ExecuteAsync();

                // Create empty set by adding and removing a placeholder
                // This ensures the key exists as an empty set
                await _database.SetAddAsync(key, "__placeholder__");
                await _database.SetRemoveAsync(key, "__placeholder__");
                await _database.KeyExpireAsync(key, FeedParticipantsCacheConstants.CacheTtl);

                Interlocked.Increment(ref _writeOperations);
                _logger.LogDebug("Set empty participants cache for feed {FeedId}", feedId.Value);
                return;
            }

            // Convert string[] to RedisValue[]
            var values = participantList
                .Select(p => (RedisValue)p)
                .ToArray();

            // Atomic: DEL (clean slate) + SADD (add all) + EXPIRE (set TTL)
            var txn = _database.CreateTransaction();

            _ = txn.KeyDeleteAsync(key);
            _ = txn.SetAddAsync(key, values);
            _ = txn.KeyExpireAsync(key, FeedParticipantsCacheConstants.CacheTtl);

            await txn.ExecuteAsync();

            Interlocked.Increment(ref _writeOperations);
            _logger.LogDebug(
                "Set {ParticipantCount} participants in cache for feed {FeedId}",
                participantList.Count,
                feedId.Value);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeErrors);
            _logger.LogWarning(
                ex,
                "Failed to set participants in cache for feed {FeedId}. Continuing without cache.",
                feedId.Value);
            // Log and continue - cache failure should not break the write path
        }
    }

    /// <inheritdoc />
    public async Task AddParticipantAsync(FeedId feedId, string participantAddress)
    {
        var key = GetParticipantsRedisKey(feedId);

        try
        {
            // Check if key exists - only add if cache is populated
            // If key doesn't exist, don't create a partial cache entry
            if (!await _database.KeyExistsAsync(key))
            {
                _logger.LogDebug(
                    "Skipping cache add for participant {Address} - feed {FeedId} cache not populated",
                    TruncateAddress(participantAddress),
                    feedId.Value);
                return;
            }

            // Atomic: SADD (add single participant) + EXPIRE (refresh TTL)
            var transaction = _database.CreateTransaction();

            _ = transaction.SetAddAsync(key, participantAddress);
            _ = transaction.KeyExpireAsync(key, FeedParticipantsCacheConstants.CacheTtl);

            await transaction.ExecuteAsync();

            Interlocked.Increment(ref _writeOperations);
            _logger.LogDebug(
                "Added participant {Address} to cache for feed {FeedId}",
                TruncateAddress(participantAddress),
                feedId.Value);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeErrors);
            _logger.LogWarning(
                ex,
                "Failed to add participant {Address} to cache for feed {FeedId}. Continuing without cache update.",
                TruncateAddress(participantAddress),
                feedId.Value);
            // Log and continue - cache failure should not break the write path
        }
    }

    /// <inheritdoc />
    public async Task RemoveParticipantAsync(FeedId feedId, string participantAddress)
    {
        var key = GetParticipantsRedisKey(feedId);

        try
        {
            // SREM removes a single member from the set (idempotent)
            await _database.SetRemoveAsync(key, participantAddress);

            _logger.LogDebug(
                "Removed participant {Address} from cache for feed {FeedId}",
                TruncateAddress(participantAddress),
                feedId.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to remove participant {Address} from cache for feed {FeedId}.",
                TruncateAddress(participantAddress),
                feedId.Value);
            // Log and continue - removal failure is not critical
        }
    }

    #endregion

    #region Part B: Key Generations (JSON Blob)

    /// <inheritdoc />
    public async Task<CachedKeyGenerations?> GetKeyGenerationsAsync(FeedId feedId)
    {
        var key = GetKeyGenerationsRedisKey(feedId);

        try
        {
            // Get JSON string from Redis
            var json = await _database.StringGetAsync(key);

            if (json.IsNullOrEmpty)
            {
                Interlocked.Increment(ref _cacheMisses);
                _logger.LogDebug("Cache miss for key generations {FeedId}", feedId.Value);
                return null;
            }

            // Refresh TTL on cache hit (sliding expiration)
            await _database.KeyExpireAsync(key, FeedParticipantsCacheConstants.CacheTtl);

            // Deserialize JSON
            var result = JsonSerializer.Deserialize<CachedKeyGenerations>(json.ToString());

            if (result == null)
            {
                Interlocked.Increment(ref _readErrors);
                _logger.LogWarning(
                    "Failed to deserialize key generations from cache for feed {FeedId}. Returning null.",
                    feedId.Value);
                return null;
            }

            Interlocked.Increment(ref _cacheHits);
            _logger.LogDebug(
                "Cache hit for key generations {FeedId} - {KeyCount} generations",
                feedId.Value,
                result.KeyGenerations.Count);

            return result;
        }
        catch (JsonException ex)
        {
            Interlocked.Increment(ref _readErrors);
            _logger.LogWarning(
                ex,
                "Failed to deserialize key generations from cache for feed {FeedId}. Returning null (cache miss).",
                feedId.Value);
            return null;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _readErrors);
            _logger.LogWarning(
                ex,
                "Failed to read key generations from cache for feed {FeedId}. Returning null (cache miss).",
                feedId.Value);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetKeyGenerationsAsync(FeedId feedId, CachedKeyGenerations keyGenerations)
    {
        var key = GetKeyGenerationsRedisKey(feedId);

        try
        {
            // Serialize to JSON
            var json = JsonSerializer.Serialize(keyGenerations);

            // SET with expiry
            await _database.StringSetAsync(key, json, FeedParticipantsCacheConstants.CacheTtl);

            Interlocked.Increment(ref _writeOperations);
            _logger.LogDebug(
                "Set {KeyCount} key generations in cache for feed {FeedId}",
                keyGenerations.KeyGenerations.Count,
                feedId.Value);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeErrors);
            _logger.LogWarning(
                ex,
                "Failed to set key generations in cache for feed {FeedId}. Continuing without cache.",
                feedId.Value);
            // Log and continue - cache failure should not break the write path
        }
    }

    /// <inheritdoc />
    public async Task InvalidateKeyGenerationsAsync(FeedId feedId)
    {
        var key = GetKeyGenerationsRedisKey(feedId);

        try
        {
            await _database.KeyDeleteAsync(key);

            _logger.LogDebug("Invalidated key generations cache for feed {FeedId}", feedId.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to invalidate key generations cache for feed {FeedId}.",
                feedId.Value);
            // Log and continue - invalidation failure is not critical
        }
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Gets the Redis key for a feed's participants cache, including the instance prefix.
    /// </summary>
    private string GetParticipantsRedisKey(FeedId feedId)
    {
        return $"{_keyPrefix}{FeedParticipantsCacheConstants.GetParticipantsKey(feedId.Value.ToString())}";
    }

    /// <summary>
    /// Gets the Redis key for a feed's key generations cache, including the instance prefix.
    /// </summary>
    private string GetKeyGenerationsRedisKey(FeedId feedId)
    {
        return $"{_keyPrefix}{FeedParticipantsCacheConstants.GetKeyGenerationsKey(feedId.Value.ToString())}";
    }

    /// <summary>
    /// Truncates address for logging (privacy).
    /// </summary>
    private static string TruncateAddress(string address)
    {
        return address.Length > 20 ? address.Substring(0, 20) + "..." : address;
    }

    #endregion
}
