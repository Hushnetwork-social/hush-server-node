using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace HushNode.Caching;

/// <summary>
/// Service for caching user feed lists in Redis.
/// Implements cache-aside pattern with in-place updates for FEAT-049.
/// Uses Redis SET to store feed IDs per user.
/// </summary>
public class UserFeedsCacheService : IUserFeedsCacheService
{
    private readonly IDatabase _database;
    private readonly string _keyPrefix;
    private readonly ILogger<UserFeedsCacheService> _logger;

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
    /// Creates a new instance of UserFeedsCacheService.
    /// </summary>
    /// <param name="connectionMultiplexer">Redis connection multiplexer.</param>
    /// <param name="keyPrefix">Prefix for all Redis keys (e.g., "HushFeeds:").</param>
    /// <param name="logger">Logger instance.</param>
    public UserFeedsCacheService(
        IConnectionMultiplexer connectionMultiplexer,
        string keyPrefix,
        ILogger<UserFeedsCacheService> logger)
    {
        _database = connectionMultiplexer.GetDatabase();
        _keyPrefix = keyPrefix;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FeedId>?> GetUserFeedsAsync(string userPublicAddress)
    {
        var key = GetKey(userPublicAddress);

        try
        {
            // Check if key exists (cache miss detection)
            if (!await _database.KeyExistsAsync(key))
            {
                Interlocked.Increment(ref _cacheMisses);
                _logger.LogDebug("Cache miss for user {UserId}", TruncateUserId(userPublicAddress));
                return null;
            }

            // Get all feed IDs from SET (SMEMBERS)
            var members = await _database.SetMembersAsync(key);

            // Refresh TTL on cache hit
            await _database.KeyExpireAsync(key, UserFeedsCacheConstants.CacheTtl);

            if (members.Length == 0)
            {
                Interlocked.Increment(ref _cacheHits);
                _logger.LogDebug("Cache hit for user {UserId} - empty set", TruncateUserId(userPublicAddress));
                return Array.Empty<FeedId>();
            }

            // Convert RedisValue[] to FeedId[]
            var feedIds = new List<FeedId>(members.Length);
            foreach (var member in members)
            {
                if (Guid.TryParse(member.ToString(), out var guid))
                {
                    feedIds.Add(new FeedId(guid));
                }
                else
                {
                    _logger.LogWarning("Invalid FeedId in cache: {Value}", member.ToString());
                }
            }

            Interlocked.Increment(ref _cacheHits);
            _logger.LogDebug(
                "Cache hit for user {UserId} - {FeedCount} feeds",
                TruncateUserId(userPublicAddress),
                feedIds.Count);

            return feedIds;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _readErrors);
            _logger.LogWarning(
                ex,
                "Failed to read feeds from cache for user {UserId}. Returning null (cache miss).",
                TruncateUserId(userPublicAddress));
            // Return null on error - caller should fall back to database
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetUserFeedsAsync(string userPublicAddress, IEnumerable<FeedId> feedIds)
    {
        var feedIdList = feedIds.ToList();
        var key = GetKey(userPublicAddress);

        try
        {
            if (feedIdList.Count == 0)
            {
                // Don't cache empty results - delete key if it exists
                await _database.KeyDeleteAsync(key);
                _logger.LogDebug("No feeds to cache for user {UserId} - key deleted", TruncateUserId(userPublicAddress));
                return;
            }

            // Convert FeedId[] to RedisValue[]
            var values = feedIdList
                .Select(f => (RedisValue)f.Value.ToString())
                .ToArray();

            // Atomic: DEL (clean slate) + SADD (add all) + EXPIRE (set TTL)
            var transaction = _database.CreateTransaction();

            _ = transaction.KeyDeleteAsync(key);
            _ = transaction.SetAddAsync(key, values);
            _ = transaction.KeyExpireAsync(key, UserFeedsCacheConstants.CacheTtl);

            await transaction.ExecuteAsync();

            Interlocked.Increment(ref _writeOperations);
            _logger.LogDebug(
                "Set {FeedCount} feeds in cache for user {UserId}",
                feedIdList.Count,
                TruncateUserId(userPublicAddress));
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeErrors);
            _logger.LogWarning(
                ex,
                "Failed to set feeds in cache for user {UserId}. Continuing without cache.",
                TruncateUserId(userPublicAddress));
            // Log and continue - cache failure should not break the write path
        }
    }

    /// <inheritdoc />
    public async Task AddFeedToUserCacheAsync(string userPublicAddress, FeedId feedId)
    {
        var key = GetKey(userPublicAddress);

        try
        {
            // Check if key exists - only add if cache is populated
            // If key doesn't exist, don't create a partial cache entry
            if (!await _database.KeyExistsAsync(key))
            {
                _logger.LogDebug(
                    "Skipping cache add for feed {FeedId} - user {UserId} cache not populated",
                    feedId.Value,
                    TruncateUserId(userPublicAddress));
                return;
            }

            // Atomic: SADD (add single feed) + EXPIRE (refresh TTL)
            var transaction = _database.CreateTransaction();

            _ = transaction.SetAddAsync(key, feedId.Value.ToString());
            _ = transaction.KeyExpireAsync(key, UserFeedsCacheConstants.CacheTtl);

            await transaction.ExecuteAsync();

            Interlocked.Increment(ref _writeOperations);
            _logger.LogDebug(
                "Added feed {FeedId} to cache for user {UserId}",
                feedId.Value,
                TruncateUserId(userPublicAddress));
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeErrors);
            _logger.LogWarning(
                ex,
                "Failed to add feed {FeedId} to cache for user {UserId}. Continuing without cache update.",
                feedId.Value,
                TruncateUserId(userPublicAddress));
            // Log and continue - cache failure should not break the write path
        }
    }

    /// <inheritdoc />
    public async Task RemoveFeedFromUserCacheAsync(string userPublicAddress, FeedId feedId)
    {
        var key = GetKey(userPublicAddress);

        try
        {
            // SREM removes a single member from the set (idempotent)
            await _database.SetRemoveAsync(key, feedId.Value.ToString());

            _logger.LogDebug(
                "Removed feed {FeedId} from cache for user {UserId}",
                feedId.Value,
                TruncateUserId(userPublicAddress));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to remove feed {FeedId} from cache for user {UserId}.",
                feedId.Value,
                TruncateUserId(userPublicAddress));
            // Log and continue - removal failure is not critical
        }
    }

    /// <summary>
    /// Gets the Redis key for a user's feed list cache, including the instance prefix.
    /// </summary>
    private string GetKey(string userPublicAddress)
    {
        return $"{_keyPrefix}{UserFeedsCacheConstants.GetUserFeedsKey(userPublicAddress)}";
    }

    /// <summary>
    /// Truncates userId for logging (privacy).
    /// </summary>
    private static string TruncateUserId(string userId)
    {
        return userId.Length > 20 ? userId.Substring(0, 20) + "..." : userId;
    }
}
