using System.Text.Json;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace HushNode.Caching;

/// <summary>
/// Service for caching group members with display names in Redis.
/// Implements cache-aside pattern with TTL expiration.
/// </summary>
public class GroupMembersCacheService : IGroupMembersCacheService
{
    private readonly IDatabase _database;
    private readonly string _keyPrefix;
    private readonly ILogger<GroupMembersCacheService> _logger;

    // Cache metrics
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
    /// Creates a new instance of GroupMembersCacheService.
    /// </summary>
    /// <param name="connectionMultiplexer">Redis connection multiplexer.</param>
    /// <param name="keyPrefix">Prefix for all Redis keys (e.g., "HushFeeds:").</param>
    /// <param name="logger">Logger instance.</param>
    public GroupMembersCacheService(
        IConnectionMultiplexer connectionMultiplexer,
        string keyPrefix,
        ILogger<GroupMembersCacheService> logger)
    {
        _database = connectionMultiplexer.GetDatabase();
        _keyPrefix = keyPrefix;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CachedGroupMembers?> GetGroupMembersAsync(FeedId feedId)
    {
        var key = GetRedisKey(feedId);

        try
        {
            var json = await _database.StringGetAsync(key);

            if (json.IsNullOrEmpty)
            {
                Interlocked.Increment(ref _cacheMisses);
                _logger.LogDebug("Cache miss for group members {FeedId}", TruncateFeedId(feedId));
                return null;
            }

            // Refresh TTL on cache hit (sliding expiration)
            await _database.KeyExpireAsync(key, GroupMembersCacheConstants.CacheTtl);

            var result = JsonSerializer.Deserialize<CachedGroupMembers>(json.ToString());

            if (result == null)
            {
                Interlocked.Increment(ref _readErrors);
                _logger.LogWarning(
                    "Failed to deserialize group members from cache for feed {FeedId}",
                    TruncateFeedId(feedId));
                return null;
            }

            Interlocked.Increment(ref _cacheHits);
            _logger.LogDebug(
                "Cache hit for group members {FeedId} - {MemberCount} members",
                TruncateFeedId(feedId),
                result.Members.Count);

            return result;
        }
        catch (JsonException ex)
        {
            Interlocked.Increment(ref _readErrors);
            _logger.LogWarning(
                ex,
                "Failed to deserialize group members from cache for feed {FeedId}",
                TruncateFeedId(feedId));
            return null;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _readErrors);
            _logger.LogWarning(
                ex,
                "Failed to read group members from cache for feed {FeedId}",
                TruncateFeedId(feedId));
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetGroupMembersAsync(FeedId feedId, CachedGroupMembers members)
    {
        var key = GetRedisKey(feedId);

        try
        {
            var json = JsonSerializer.Serialize(members);

            await _database.StringSetAsync(key, json, GroupMembersCacheConstants.CacheTtl);

            Interlocked.Increment(ref _writeOperations);
            _logger.LogDebug(
                "Cached {MemberCount} group members for feed {FeedId}",
                members.Members.Count,
                TruncateFeedId(feedId));
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeErrors);
            _logger.LogWarning(
                ex,
                "Failed to cache group members for feed {FeedId}",
                TruncateFeedId(feedId));
        }
    }

    /// <inheritdoc />
    public async Task InvalidateGroupMembersAsync(FeedId feedId)
    {
        var key = GetRedisKey(feedId);

        try
        {
            await _database.KeyDeleteAsync(key);
            _logger.LogDebug("Invalidated group members cache for feed {FeedId}", TruncateFeedId(feedId));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to invalidate group members cache for feed {FeedId}",
                TruncateFeedId(feedId));
        }
    }

    /// <inheritdoc />
    public async Task InvalidateGroupMembersForUserAsync(string publicSigningAddress, IEnumerable<FeedId> feedIds)
    {
        var feedIdList = feedIds.ToList();

        if (feedIdList.Count == 0)
        {
            _logger.LogDebug(
                "No feeds to invalidate for user {Address}",
                TruncateAddress(publicSigningAddress));
            return;
        }

        _logger.LogDebug(
            "Invalidating group members cache for {FeedCount} feeds due to name change by {Address}",
            feedIdList.Count,
            TruncateAddress(publicSigningAddress));

        // Invalidate each feed's group members cache
        foreach (var feedId in feedIdList)
        {
            await InvalidateGroupMembersAsync(feedId);
        }
    }

    /// <summary>
    /// Gets the Redis key for a feed's group members cache.
    /// </summary>
    private string GetRedisKey(FeedId feedId)
    {
        return $"{_keyPrefix}{GroupMembersCacheConstants.GetGroupMembersKey(feedId.Value.ToString())}";
    }

    /// <summary>
    /// Truncates feed ID for logging.
    /// </summary>
    private static string TruncateFeedId(FeedId feedId)
    {
        var id = feedId.Value.ToString();
        return id.Length > 8 ? id.Substring(0, 8) + "..." : id;
    }

    /// <summary>
    /// Truncates address for logging.
    /// </summary>
    private static string TruncateAddress(string address)
    {
        return address.Length > 20 ? address.Substring(0, 20) + "..." : address;
    }
}
