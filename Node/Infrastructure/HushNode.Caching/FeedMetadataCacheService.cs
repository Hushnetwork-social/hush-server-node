using System.Text.Json;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace HushNode.Caching;

/// <summary>
/// Service for caching per-user feed metadata in Redis.
/// FEAT-060: lastBlockIndex-only methods (legacy, kept for backward compat until all callers migrate).
/// FEAT-065: Full 6-field metadata methods (new, single source of truth).
/// Implements graceful degradation: cache failures return null/false and are logged.
/// </summary>
public class FeedMetadataCacheService : IFeedMetadataCacheService
{
    private readonly IDatabase _database;
    private readonly string _keyPrefix;
    private readonly ILogger<FeedMetadataCacheService> _logger;

    // Cache metrics (simple counters for observability)
    private long _cacheHits;
    private long _cacheMisses;
    private long _writeOperations;
    private long _writeErrors;
    private long _readErrors;

    public long CacheHits => Interlocked.Read(ref _cacheHits);
    public long CacheMisses => Interlocked.Read(ref _cacheMisses);
    public long WriteOperations => Interlocked.Read(ref _writeOperations);
    public long WriteErrors => Interlocked.Read(ref _writeErrors);
    public long ReadErrors => Interlocked.Read(ref _readErrors);

    public FeedMetadataCacheService(
        IConnectionMultiplexer connectionMultiplexer,
        string keyPrefix,
        ILogger<FeedMetadataCacheService> logger)
    {
        _database = connectionMultiplexer.GetDatabase();
        _keyPrefix = keyPrefix;
        _logger = logger;
    }

    // ==========================================
    // FEAT-060 Legacy Methods (kept until Phase 4 migrates all callers)
    // ==========================================

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<FeedId, BlockIndex>?> GetAllLastBlockIndexesAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return null;

        var key = GetKey(userId);

        try
        {
            var entries = await _database.HashGetAllAsync(key);

            if (entries.Length == 0)
            {
                Interlocked.Increment(ref _cacheMisses);
                _logger.LogDebug("Cache miss for feed metadata hash user={UserId}", userId);
                return null;
            }

            var result = new Dictionary<FeedId, BlockIndex>();
            foreach (var entry in entries)
            {
                var lastBlockIndex = ParseLastBlockIndex(entry.Value);
                if (lastBlockIndex.HasValue)
                {
                    var feedId = FeedIdHandler.CreateFromString(entry.Name!);
                    result[feedId] = new BlockIndex(lastBlockIndex.Value);
                }
            }

            Interlocked.Increment(ref _cacheHits);
            _logger.LogDebug("Cache hit for feed metadata hash user={UserId} count={Count}", userId, result.Count);
            return result;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _readErrors);
            _logger.LogWarning(ex, "Failed to read feed metadata hash for user={UserId}. Returning null.", userId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetLastBlockIndexAsync(string userId, FeedId feedId, BlockIndex lastBlockIndex)
    {
        if (string.IsNullOrEmpty(userId))
            return false;

        var key = GetKey(userId);

        try
        {
            var json = SerializeLastBlockIndex(lastBlockIndex.Value);
            await _database.HashSetAsync(key, feedId.ToString(), json);
            await _database.KeyExpireAsync(key, FeedMetadataCacheConstants.CacheTtl);

            Interlocked.Increment(ref _writeOperations);
            _logger.LogDebug("Set feed metadata user={UserId} feed={FeedId} lastBlockIndex={LastBlockIndex}",
                userId, feedId, lastBlockIndex.Value);
            return true;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeErrors);
            _logger.LogWarning(ex, "Failed to set feed metadata for user={UserId} feed={FeedId}.", userId, feedId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetMultipleLastBlockIndexesAsync(string userId, IReadOnlyDictionary<FeedId, BlockIndex> blockIndexes)
    {
        if (string.IsNullOrEmpty(userId) || blockIndexes == null || blockIndexes.Count == 0)
            return false;

        var key = GetKey(userId);

        try
        {
            var entries = blockIndexes
                .Select(p => new HashEntry(p.Key.ToString(), SerializeLastBlockIndex(p.Value.Value)))
                .ToArray();

            await _database.HashSetAsync(key, entries);
            await _database.KeyExpireAsync(key, FeedMetadataCacheConstants.CacheTtl);

            Interlocked.Increment(ref _writeOperations);
            _logger.LogDebug("Bulk set feed metadata hash user={UserId} count={Count}", userId, blockIndexes.Count);
            return true;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeErrors);
            _logger.LogWarning(ex, "Failed to bulk set feed metadata for user={UserId}.", userId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RemoveFeedMetaAsync(string userId, FeedId feedId)
    {
        if (string.IsNullOrEmpty(userId))
            return false;

        var key = GetKey(userId);

        try
        {
            var removed = await _database.HashDeleteAsync(key, feedId.ToString());
            Interlocked.Increment(ref _writeOperations);
            _logger.LogDebug("Removed feed metadata user={UserId} feed={FeedId} removed={Removed}",
                userId, feedId, removed);
            return removed;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeErrors);
            _logger.LogWarning(ex, "Failed to remove feed metadata for user={UserId} feed={FeedId}.", userId, feedId);
            return false;
        }
    }

    // ==========================================
    // FEAT-065 Full Metadata Methods
    // ==========================================

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<FeedId, FeedMetadataEntry>?> GetAllFeedMetadataAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return null;

        var key = GetKey(userId);

        try
        {
            var entries = await _database.HashGetAllAsync(key);

            if (entries.Length == 0)
            {
                Interlocked.Increment(ref _cacheMisses);
                _logger.LogDebug("Cache miss for feed metadata hash user={UserId}", userId);
                return null;
            }

            var result = new Dictionary<FeedId, FeedMetadataEntry>();
            foreach (var entry in entries)
            {
                var metadata = DeserializeFeedMetadata(entry.Value);
                if (metadata == null)
                    continue;

                // Lazy migration: legacy FEAT-060 entries (missing title/participants) trigger cache miss
                if (metadata.IsLegacyFormat)
                {
                    Interlocked.Increment(ref _cacheMisses);
                    _logger.LogDebug(
                        "Legacy FEAT-060 format detected for user={UserId}, treating as cache miss for lazy migration",
                        userId);
                    return null;
                }

                var feedId = FeedIdHandler.CreateFromString(entry.Name!);
                result[feedId] = metadata;
            }

            Interlocked.Increment(ref _cacheHits);
            _logger.LogDebug("Cache hit for full feed metadata user={UserId} count={Count}", userId, result.Count);
            return result;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _readErrors);
            _logger.LogWarning(ex, "Failed to read full feed metadata for user={UserId}. Returning null.", userId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetFeedMetadataAsync(string userId, FeedId feedId, FeedMetadataEntry entry)
    {
        if (string.IsNullOrEmpty(userId))
            return false;

        var key = GetKey(userId);

        try
        {
            var json = JsonSerializer.Serialize(entry);
            await _database.HashSetAsync(key, feedId.ToString(), json);
            await _database.KeyExpireAsync(key, FeedMetadataCacheConstants.CacheTtl);

            Interlocked.Increment(ref _writeOperations);
            _logger.LogDebug("Set full feed metadata user={UserId} feed={FeedId} title={Title}",
                userId, feedId, entry.Title);
            return true;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeErrors);
            _logger.LogWarning(ex, "Failed to set full feed metadata for user={UserId} feed={FeedId}.", userId, feedId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetMultipleFeedMetadataAsync(string userId, IReadOnlyDictionary<FeedId, FeedMetadataEntry> entries)
    {
        if (string.IsNullOrEmpty(userId) || entries == null || entries.Count == 0)
            return false;

        var key = GetKey(userId);

        try
        {
            var hashEntries = entries
                .Select(p => new HashEntry(p.Key.ToString(), JsonSerializer.Serialize(p.Value)))
                .ToArray();

            await _database.HashSetAsync(key, hashEntries);
            await _database.KeyExpireAsync(key, FeedMetadataCacheConstants.CacheTtl);

            Interlocked.Increment(ref _writeOperations);
            _logger.LogDebug("Bulk set full feed metadata user={UserId} count={Count}", userId, entries.Count);
            return true;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeErrors);
            _logger.LogWarning(ex, "Failed to bulk set full feed metadata for user={UserId}.", userId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RemoveFeedMetadataAsync(string userId, FeedId feedId)
    {
        if (string.IsNullOrEmpty(userId))
            return false;

        var key = GetKey(userId);

        try
        {
            var removed = await _database.HashDeleteAsync(key, feedId.ToString());
            Interlocked.Increment(ref _writeOperations);
            _logger.LogDebug("Removed feed metadata user={UserId} feed={FeedId} removed={Removed}",
                userId, feedId, removed);
            return removed;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeErrors);
            _logger.LogWarning(ex, "Failed to remove feed metadata for user={UserId} feed={FeedId}.", userId, feedId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateFeedTitleAsync(string userId, FeedId feedId, string newTitle)
    {
        if (string.IsNullOrEmpty(userId))
            return false;

        var key = GetKey(userId);

        try
        {
            // Read existing entry
            var existingJson = await _database.HashGetAsync(key, feedId.ToString());
            if (existingJson.IsNullOrEmpty)
            {
                _logger.LogDebug(
                    "UpdateFeedTitleAsync: entry not found for user={UserId} feed={FeedId}, skipping",
                    userId, feedId);
                return false;
            }

            var metadata = DeserializeFeedMetadata(existingJson!);
            if (metadata == null || metadata.IsLegacyFormat)
            {
                _logger.LogDebug(
                    "UpdateFeedTitleAsync: invalid/legacy entry for user={UserId} feed={FeedId}, skipping",
                    userId, feedId);
                return false;
            }

            // Update title only
            metadata.Title = newTitle;

            // Write back
            var updatedJson = JsonSerializer.Serialize(metadata);
            await _database.HashSetAsync(key, feedId.ToString(), updatedJson);
            await _database.KeyExpireAsync(key, FeedMetadataCacheConstants.CacheTtl);

            Interlocked.Increment(ref _writeOperations);
            _logger.LogDebug("Updated feed title user={UserId} feed={FeedId} newTitle={NewTitle}",
                userId, feedId, newTitle);
            return true;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeErrors);
            _logger.LogWarning(ex, "Failed to update feed title for user={UserId} feed={FeedId}.", userId, feedId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateLastBlockIndexAsync(string userId, FeedId feedId, BlockIndex lastBlockIndex)
    {
        if (string.IsNullOrEmpty(userId))
            return false;

        var key = GetKey(userId);

        try
        {
            var existingJson = await _database.HashGetAsync(key, feedId.ToString());
            if (existingJson.IsNullOrEmpty)
            {
                _logger.LogDebug(
                    "UpdateLastBlockIndexAsync: entry not found for user={UserId} feed={FeedId}, skipping",
                    userId, feedId);
                return false;
            }

            var metadata = DeserializeFeedMetadata(existingJson!);
            if (metadata == null || metadata.IsLegacyFormat)
            {
                _logger.LogDebug(
                    "UpdateLastBlockIndexAsync: invalid/legacy entry for user={UserId} feed={FeedId}, skipping",
                    userId, feedId);
                return false;
            }

            metadata.LastBlockIndex = lastBlockIndex.Value;

            var updatedJson = JsonSerializer.Serialize(metadata);
            await _database.HashSetAsync(key, feedId.ToString(), updatedJson);
            await _database.KeyExpireAsync(key, FeedMetadataCacheConstants.CacheTtl);

            Interlocked.Increment(ref _writeOperations);
            _logger.LogDebug("Updated feed lastBlockIndex user={UserId} feed={FeedId} lastBlockIndex={LastBlockIndex}",
                userId, feedId, lastBlockIndex.Value);
            return true;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeErrors);
            _logger.LogWarning(ex, "Failed to update feed lastBlockIndex for user={UserId} feed={FeedId}.", userId, feedId);
            return false;
        }
    }

    // ==========================================
    // Private Helpers
    // ==========================================

    private string GetKey(string userId)
    {
        return $"{_keyPrefix}{FeedMetadataCacheConstants.GetFeedMetaHashKey(userId)}";
    }

    /// <summary>
    /// Serializes a lastBlockIndex value to JSON format (FEAT-060 legacy).
    /// </summary>
    private static string SerializeLastBlockIndex(long lastBlockIndex)
    {
        return JsonSerializer.Serialize(new { lastBlockIndex });
    }

    /// <summary>
    /// Parses a lastBlockIndex from JSON format (FEAT-060 legacy).
    /// </summary>
    private static long? ParseLastBlockIndex(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("lastBlockIndex", out var prop) && prop.TryGetInt64(out var value))
                return value;
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Deserializes a FeedMetadataEntry from JSON. Returns null on invalid JSON.
    /// </summary>
    private static FeedMetadataEntry? DeserializeFeedMetadata(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<FeedMetadataEntry>(json);
        }
        catch
        {
            return null;
        }
    }
}
