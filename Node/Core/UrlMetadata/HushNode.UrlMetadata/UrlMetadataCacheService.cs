using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HushNode.Notifications;
using HushNode.UrlMetadata.Models;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace HushNode.UrlMetadata;

/// <summary>
/// Provides caching for URL metadata using Redis.
/// </summary>
public interface IUrlMetadataCacheService
{
    /// <summary>
    /// Gets cached metadata for a URL, or fetches and caches it if not found.
    /// </summary>
    /// <param name="url">The URL to get metadata for.</param>
    /// <param name="fetchFunc">Function to fetch metadata if not cached.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached or freshly fetched metadata.</returns>
    Task<UrlMetadataResult?> GetOrFetchAsync(
        string url,
        Func<Task<UrlMetadataResult>> fetchFunc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cached metadata for a URL without fetching.
    /// </summary>
    /// <param name="url">The URL to get cached metadata for.</param>
    /// <returns>Cached metadata or null if not found.</returns>
    Task<UrlMetadataResult?> GetAsync(string url);

    /// <summary>
    /// Stores metadata in the cache.
    /// </summary>
    /// <param name="result">The metadata to cache.</param>
    Task SetAsync(UrlMetadataResult result);
}

/// <summary>
/// Redis-backed implementation of URL metadata caching.
/// Uses cache-aside pattern with 24-hour TTL.
/// </summary>
public class UrlMetadataCacheService : IUrlMetadataCacheService
{
    private readonly RedisConnectionManager _redis;
    private readonly ILogger<UrlMetadataCacheService> _logger;

    /// <summary>
    /// Cache TTL: 24 hours.
    /// </summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    /// <summary>
    /// Redis key prefix for URL metadata cache.
    /// </summary>
    private const string KeyPrefix = "url-metadata:";

    public UrlMetadataCacheService(
        RedisConnectionManager redis,
        ILogger<UrlMetadataCacheService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<UrlMetadataResult?> GetOrFetchAsync(
        string url,
        Func<Task<UrlMetadataResult>> fetchFunc,
        CancellationToken cancellationToken = default)
    {
        // Try to get from cache first
        var cached = await GetAsync(url);
        if (cached != null)
        {
            _logger.LogDebug("Cache hit for URL: {Url}", url);
            return cached;
        }

        _logger.LogDebug("Cache miss for URL: {Url}", url);

        // Fetch fresh data
        var result = await fetchFunc();

        // Only cache successful results - don't cache failures so they can be retried
        if (result.Success)
        {
            await SetAsync(result);
        }
        else
        {
            _logger.LogDebug("Not caching failed result for URL: {Url}, Error: {Error}", url, result.ErrorMessage);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<UrlMetadataResult?> GetAsync(string url)
    {
        try
        {
            var key = GetCacheKey(url);
            var value = await _redis.Database.StringGetAsync(key);

            if (!value.HasValue)
                return null;

            return JsonSerializer.Deserialize<UrlMetadataResult>(value.ToString());
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis connection error while getting cached metadata for {Url}", url);
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize cached metadata for {Url}", url);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetAsync(UrlMetadataResult result)
    {
        try
        {
            var key = GetCacheKey(result.Url);
            var json = JsonSerializer.Serialize(result);

            await _redis.Database.StringSetAsync(key, json, CacheTtl);

            _logger.LogDebug("Cached metadata for URL: {Url}, TTL: {Ttl}", result.Url, CacheTtl);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis connection error while caching metadata for {Url}", result.Url);
        }
    }

    /// <summary>
    /// Generates a Redis key for the given URL.
    /// Uses MD5 hash of normalized URL.
    /// </summary>
    private string GetCacheKey(string url)
    {
        var normalizedUrl = NormalizeUrl(url);
        var hash = ComputeMd5Hash(normalizedUrl);
        return $"{_redis.KeyPrefix}{KeyPrefix}{hash}";
    }

    /// <summary>
    /// Normalizes a URL for consistent cache key generation.
    /// </summary>
    private static string NormalizeUrl(string url)
    {
        // Lowercase and remove trailing slash
        var normalized = url.ToLowerInvariant().TrimEnd('/');
        return normalized;
    }

    /// <summary>
    /// Computes MD5 hash of a string.
    /// </summary>
    private static string ComputeMd5Hash(string input)
    {
        var inputBytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = MD5.HashData(inputBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
