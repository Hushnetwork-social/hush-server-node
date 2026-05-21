using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace HushNode.Caching;

public sealed class ElectionVoidPublicCacheService : IElectionVoidPublicCacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDatabase _database;
    private readonly string _keyPrefix;
    private readonly ILogger<ElectionVoidPublicCacheService> _logger;

    public ElectionVoidPublicCacheService(
        IConnectionMultiplexer connectionMultiplexer,
        string keyPrefix,
        ILogger<ElectionVoidPublicCacheService> logger)
    {
        _database = connectionMultiplexer.GetDatabase();
        _keyPrefix = keyPrefix;
        _logger = logger;
    }

    public Task<ElectionVoidPublicCacheEnvelope?> GetPublicStatusAsync(string electionId) =>
        GetAsync(GetKey(ElectionVoidPublicCacheConstants.GetPublicStatusKey(electionId)), electionId);

    public Task<ElectionVoidPublicCacheEnvelope?> GetPublicArtifactAsync(
        string electionId,
        Guid voidDecisionId,
        Guid publicationAttemptId,
        string artifactName) =>
        GetAsync(
            GetKey(ElectionVoidPublicCacheConstants.GetPublicArtifactKey(
                electionId,
                voidDecisionId,
                publicationAttemptId,
                artifactName)),
            electionId);

    public Task SetPublicStatusAsync(
        string electionId,
        ElectionVoidPublicCacheEnvelope envelope) =>
        SetAsync(GetKey(ElectionVoidPublicCacheConstants.GetPublicStatusKey(electionId)), electionId, envelope);

    public Task SetPublicArtifactAsync(
        string electionId,
        Guid voidDecisionId,
        Guid publicationAttemptId,
        string artifactName,
        ElectionVoidPublicCacheEnvelope envelope) =>
        SetAsync(
            GetKey(ElectionVoidPublicCacheConstants.GetPublicArtifactKey(
                electionId,
                voidDecisionId,
                publicationAttemptId,
                artifactName)),
            electionId,
            envelope);

    private async Task<ElectionVoidPublicCacheEnvelope?> GetAsync(string key, string electionId)
    {
        try
        {
            var value = await _database.StringGetAsync(key);
            if (value.IsNullOrEmpty)
            {
                return null;
            }

            await _database.KeyExpireAsync(key, ElectionVoidPublicCacheConstants.CacheTtl);
            return JsonSerializer.Deserialize<ElectionVoidPublicCacheEnvelope>(value!, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to read FEAT-138 public VOID cache for election {ElectionId}. Falling back to durable storage.",
                electionId);
            return null;
        }
    }

    private async Task SetAsync(string key, string electionId, ElectionVoidPublicCacheEnvelope envelope)
    {
        try
        {
            await _database.StringSetAsync(
                key,
                JsonSerializer.Serialize(envelope, JsonOptions),
                ElectionVoidPublicCacheConstants.CacheTtl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to populate FEAT-138 public VOID cache for election {ElectionId}. Durable storage remains authoritative.",
                electionId);
        }
    }

    private string GetKey(string key) => $"{_keyPrefix}{key}";
}
