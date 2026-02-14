using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace HushNode.Caching;

/// <summary>
/// Service for caching identity display names in a global Redis Hash (FEAT-065 E2).
/// Key: {prefix}identities:display_names
/// Fields: {publicAddress} → displayName (plain string, not JSON).
/// No TTL — updated on identity change events.
/// </summary>
public class IdentityDisplayNameCacheService : IIdentityDisplayNameCacheService
{
    private readonly IDatabase _database;
    private readonly string _hashKey;
    private readonly ILogger<IdentityDisplayNameCacheService> _logger;

    // Cache metrics
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

    public IdentityDisplayNameCacheService(
        IConnectionMultiplexer connectionMultiplexer,
        string keyPrefix,
        ILogger<IdentityDisplayNameCacheService> logger)
    {
        _database = connectionMultiplexer.GetDatabase();
        _hashKey = IdentityDisplayNameCacheConstants.GetDisplayNamesKey(keyPrefix);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string?>?> GetDisplayNamesAsync(IEnumerable<string> addresses)
    {
        var addressList = addresses?.ToList();
        if (addressList == null || addressList.Count == 0)
            return new Dictionary<string, string?>();

        try
        {
            var fields = addressList.Select(a => (RedisValue)a).ToArray();
            var values = await _database.HashGetAsync(_hashKey, fields);

            var result = new Dictionary<string, string?>();
            for (int i = 0; i < addressList.Count; i++)
            {
                if (values[i].IsNullOrEmpty)
                {
                    result[addressList[i]] = null;
                    Interlocked.Increment(ref _cacheMisses);
                }
                else
                {
                    result[addressList[i]] = values[i].ToString();
                    Interlocked.Increment(ref _cacheHits);
                }
            }

            _logger.LogDebug(
                "Identity display name lookup: {Total} addresses, {Hits} hits, {Misses} misses",
                addressList.Count,
                result.Count(kvp => kvp.Value != null),
                result.Count(kvp => kvp.Value == null));

            return result;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _readErrors);
            _logger.LogWarning(ex, "Failed to get display names from Redis. Returning null for degradation.");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetDisplayNameAsync(string address, string displayName)
    {
        if (string.IsNullOrEmpty(address) || string.IsNullOrEmpty(displayName))
            return false;

        try
        {
            await _database.HashSetAsync(_hashKey, address, displayName);

            Interlocked.Increment(ref _writeOperations);
            _logger.LogDebug("Set display name for address={Address} name={Name}", address, displayName);
            return true;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeErrors);
            _logger.LogWarning(ex, "Failed to set display name for address={Address}.", address);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetMultipleDisplayNamesAsync(IReadOnlyDictionary<string, string> displayNames)
    {
        if (displayNames == null || displayNames.Count == 0)
            return false;

        try
        {
            var entries = displayNames
                .Select(kvp => new HashEntry(kvp.Key, kvp.Value))
                .ToArray();

            await _database.HashSetAsync(_hashKey, entries);

            Interlocked.Increment(ref _writeOperations);
            _logger.LogDebug("Bulk set {Count} display names", displayNames.Count);
            return true;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeErrors);
            _logger.LogWarning(ex, "Failed to bulk set display names.");
            return false;
        }
    }
}
