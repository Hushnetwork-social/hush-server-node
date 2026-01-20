using System.Text.Json;
using HushNode.Events;
using HushShared.Identity.Model;
using Microsoft.Extensions.Logging;
using Olimpo;
using StackExchange.Redis;

namespace HushNode.Caching;

/// <summary>
/// Service for caching identity profiles in Redis.
/// Implements cache-aside pattern with event-driven invalidation for FEAT-048.
/// </summary>
public class IdentityCacheService : IIdentityCacheService, IHandleAsync<IdentityUpdatedEvent>
{
    private readonly IDatabase _database;
    private readonly string _keyPrefix;
    private readonly ILogger<IdentityCacheService> _logger;

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
    /// Creates a new instance of IdentityCacheService.
    /// </summary>
    /// <param name="connectionMultiplexer">Redis connection multiplexer.</param>
    /// <param name="keyPrefix">Prefix for all Redis keys (e.g., "HushFeeds:").</param>
    /// <param name="eventAggregator">Event aggregator for subscribing to IdentityUpdatedEvent.</param>
    /// <param name="logger">Logger instance.</param>
    public IdentityCacheService(
        IConnectionMultiplexer connectionMultiplexer,
        string keyPrefix,
        IEventAggregator eventAggregator,
        ILogger<IdentityCacheService> logger)
    {
        _database = connectionMultiplexer.GetDatabase();
        _keyPrefix = keyPrefix;
        _logger = logger;

        // Subscribe to identity update events for cache invalidation
        eventAggregator.Subscribe(this);

        _logger.LogInformation("IdentityCacheService initialized and subscribed to IdentityUpdatedEvent");
    }

    /// <inheritdoc />
    public async Task<Profile?> GetIdentityAsync(string publicSigningAddress)
    {
        var key = GetKey(publicSigningAddress);

        try
        {
            var value = await _database.StringGetAsync(key);

            if (value.IsNullOrEmpty)
            {
                Interlocked.Increment(ref _cacheMisses);
                _logger.LogDebug("Cache miss for identity {Address}", TruncateAddress(publicSigningAddress));
                return null;
            }

            // Deserialize the cached profile
            var profile = DeserializeProfile(value!);

            if (profile == null)
            {
                Interlocked.Increment(ref _cacheMisses);
                _logger.LogDebug("Cache miss for identity {Address} (deserialization failed)", TruncateAddress(publicSigningAddress));
                return null;
            }

            // Refresh TTL on cache hit to keep frequently accessed identities cached
            await _database.KeyExpireAsync(key, IdentityCacheConstants.CacheTtl);

            Interlocked.Increment(ref _cacheHits);
            _logger.LogDebug("Cache hit for identity {Address}", TruncateAddress(publicSigningAddress));

            return profile;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _readErrors);
            _logger.LogWarning(
                ex,
                "Failed to read identity from cache for {Address}. Returning null (cache miss).",
                TruncateAddress(publicSigningAddress));
            // Return null on error - caller should fall back to database
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetIdentityAsync(string publicSigningAddress, Profile profile)
    {
        var key = GetKey(publicSigningAddress);

        try
        {
            var serialized = SerializeProfile(profile);

            await _database.StringSetAsync(key, serialized, IdentityCacheConstants.CacheTtl);

            Interlocked.Increment(ref _writeOperations);
            _logger.LogDebug("Cached identity for {Address}", TruncateAddress(publicSigningAddress));
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeErrors);
            _logger.LogWarning(
                ex,
                "Failed to cache identity for {Address}. Continuing without cache.",
                TruncateAddress(publicSigningAddress));
            // Log and continue - cache failure should not break the read path
        }
    }

    /// <inheritdoc />
    public async Task InvalidateCacheAsync(string publicSigningAddress)
    {
        var key = GetKey(publicSigningAddress);

        try
        {
            await _database.KeyDeleteAsync(key);
            _logger.LogDebug("Invalidated cache for identity {Address}", TruncateAddress(publicSigningAddress));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to invalidate cache for identity {Address}. Cache may contain stale data.",
                TruncateAddress(publicSigningAddress));
            // Log and continue - invalidation failure is not critical
        }
    }

    /// <summary>
    /// Handles IdentityUpdatedEvent by invalidating the cache for the updated identity.
    /// </summary>
    public async Task HandleAsync(IdentityUpdatedEvent message)
    {
        _logger.LogDebug(
            "Received IdentityUpdatedEvent for {Address}, invalidating cache",
            TruncateAddress(message.PublicSigningAddress));

        await InvalidateCacheAsync(message.PublicSigningAddress);
    }

    /// <summary>
    /// Gets the Redis key for an identity's cache entry, including the instance prefix.
    /// </summary>
    private string GetKey(string publicSigningAddress)
    {
        return $"{_keyPrefix}{IdentityCacheConstants.GetIdentityKey(publicSigningAddress)}";
    }

    /// <summary>
    /// Serializes a Profile to JSON string.
    /// </summary>
    private static string SerializeProfile(Profile profile)
    {
        return JsonSerializer.Serialize(profile);
    }

    /// <summary>
    /// Deserializes a JSON string to Profile.
    /// </summary>
    private Profile? DeserializeProfile(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Profile>(json);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize Profile from cache");
            return null;
        }
    }

    /// <summary>
    /// Truncates a public address for logging purposes.
    /// </summary>
    private static string TruncateAddress(string address)
    {
        return address.Length > 20 ? address.Substring(0, 20) + "..." : address;
    }
}
