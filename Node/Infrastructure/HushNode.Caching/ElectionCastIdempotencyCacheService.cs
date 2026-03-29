using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace HushNode.Caching;

/// <summary>
/// Redis-backed cache for committed FEAT-099 election-scoped idempotency markers.
/// </summary>
public class ElectionCastIdempotencyCacheService : IElectionCastIdempotencyCacheService
{
    private const string MarkerValue = "1";

    private readonly IDatabase _database;
    private readonly string _keyPrefix;
    private readonly ILogger<ElectionCastIdempotencyCacheService> _logger;

    public ElectionCastIdempotencyCacheService(
        IConnectionMultiplexer connectionMultiplexer,
        string keyPrefix,
        ILogger<ElectionCastIdempotencyCacheService> logger)
    {
        _database = connectionMultiplexer.GetDatabase();
        _keyPrefix = keyPrefix;
        _logger = logger;
    }

    public async Task<bool?> ExistsAsync(string electionId, string idempotencyKeyHash)
    {
        var key = GetKey(electionId, idempotencyKeyHash);

        try
        {
            if (!await _database.KeyExistsAsync(key))
            {
                return null;
            }

            await _database.KeyExpireAsync(key, ElectionCastIdempotencyCacheConstants.CacheTtl);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to read FEAT-099 committed idempotency cache for election {ElectionId}. Falling back to PostgreSQL.",
                electionId);
            return null;
        }
    }

    public async Task SetAsync(string electionId, string idempotencyKeyHash)
    {
        var key = GetKey(electionId, idempotencyKeyHash);

        try
        {
            await _database.StringSetAsync(
                key,
                MarkerValue,
                ElectionCastIdempotencyCacheConstants.CacheTtl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to populate FEAT-099 committed idempotency cache for election {ElectionId}. PostgreSQL remains authoritative.",
                electionId);
        }
    }

    private string GetKey(string electionId, string idempotencyKeyHash) =>
        $"{_keyPrefix}{ElectionCastIdempotencyCacheConstants.GetCommittedMarkerKey(electionId, idempotencyKeyHash)}";
}
