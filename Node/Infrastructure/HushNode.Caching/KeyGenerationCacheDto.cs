using System.Text.Json.Serialization;

namespace HushNode.Caching;

/// <summary>
/// DTO representing a single key generation for caching in Redis.
/// Contains the generation version, block range validity, and encrypted keys per member.
/// </summary>
public record KeyGenerationCacheDto
{
    /// <summary>
    /// The key generation version number (1, 2, 3, etc.).
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; init; }

    /// <summary>
    /// The block index from which this key generation is valid.
    /// </summary>
    [JsonPropertyName("validFromBlock")]
    public long ValidFromBlock { get; init; }

    /// <summary>
    /// The block index until which this key generation is valid (exclusive).
    /// Null for the current active generation (no end block yet).
    /// </summary>
    [JsonPropertyName("validToBlock")]
    public long? ValidToBlock { get; init; }

    /// <summary>
    /// The encrypted AES keys per member.
    /// Key: Member's public signing address
    /// Value: Base64-encoded encrypted AES key
    /// </summary>
    [JsonPropertyName("encryptedKeysByMember")]
    public Dictionary<string, string> EncryptedKeysByMember { get; init; } = new();
}

/// <summary>
/// Wrapper for caching multiple key generations for a feed.
/// This is the root object serialized to Redis JSON.
/// </summary>
public record CachedKeyGenerations
{
    /// <summary>
    /// All key generations for the feed, ordered by version ascending.
    /// </summary>
    [JsonPropertyName("keyGenerations")]
    public List<KeyGenerationCacheDto> KeyGenerations { get; init; } = new();
}
