using System.Security.Cryptography;
using System.Text;

namespace HushNode.UrlMetadata.Models;

/// <summary>
/// Represents a cached URL metadata entry with expiration.
/// </summary>
public class UrlMetadataCacheEntry
{
    /// <summary>
    /// Default cache TTL (24 hours).
    /// </summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    /// <summary>
    /// Redis key prefix for URL metadata cache entries.
    /// </summary>
    public const string CacheKeyPrefix = "url-metadata:";

    /// <summary>
    /// The cache key (normalized URL hash).
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// The cached metadata result.
    /// </summary>
    public required UrlMetadataResult Metadata { get; init; }

    /// <summary>
    /// When this cache entry expires.
    /// </summary>
    public DateTime ExpiresAt { get; init; }

    /// <summary>
    /// Whether this cache entry has expired.
    /// </summary>
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

    /// <summary>
    /// Creates a new cache entry for the given metadata.
    /// </summary>
    public static UrlMetadataCacheEntry Create(UrlMetadataResult metadata, TimeSpan? ttl = null)
    {
        var effectiveTtl = ttl ?? DefaultTtl;
        return new UrlMetadataCacheEntry
        {
            Key = GenerateCacheKey(metadata.Url),
            Metadata = metadata,
            ExpiresAt = DateTime.UtcNow.Add(effectiveTtl)
        };
    }

    /// <summary>
    /// Generates a cache key from a URL by normalizing and hashing it.
    /// </summary>
    public static string GenerateCacheKey(string url)
    {
        var normalizedUrl = NormalizeUrl(url);
        var hash = ComputeMd5Hash(normalizedUrl);
        return $"{CacheKeyPrefix}{hash}";
    }

    /// <summary>
    /// Normalizes a URL for consistent cache key generation.
    /// - Converts to lowercase
    /// - Removes trailing slash
    /// </summary>
    private static string NormalizeUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return url;

        var normalized = url.ToLowerInvariant().TrimEnd('/');
        return normalized;
    }

    /// <summary>
    /// Computes an MD5 hash of the input string.
    /// </summary>
    private static string ComputeMd5Hash(string input)
    {
        var inputBytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = MD5.HashData(inputBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
