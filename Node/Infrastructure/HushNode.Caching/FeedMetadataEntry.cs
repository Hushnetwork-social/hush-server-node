using System.Text.Json.Serialization;

namespace HushNode.Caching;

/// <summary>
/// Represents a single feed's metadata stored as a JSON value in the Redis Hash (FEAT-065).
/// Each field in the user:{userId}:feed_meta Hash contains one of these entries serialized as JSON.
/// Supports lazy migration from FEAT-060's lastBlockIndex-only format.
/// </summary>
public class FeedMetadataEntry
{
    /// <summary>
    /// Feed display title (user-specific for Chat feeds).
    /// Chat: other participant's display name. Personal: own name + " (YOU)". Group: group title.
    /// Null when deserializing from legacy FEAT-060 format (indicates lazy migration needed).
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Feed type enum value (Personal=0, Chat=1, Group=2, Broadcast=3).
    /// </summary>
    [JsonPropertyName("type")]
    public int Type { get; set; }

    /// <summary>
    /// Block index of the most recent activity in this feed.
    /// </summary>
    [JsonPropertyName("lastBlockIndex")]
    public long LastBlockIndex { get; set; }

    /// <summary>
    /// Public signing addresses of all participants.
    /// Null when deserializing from legacy FEAT-060 format.
    /// </summary>
    [JsonPropertyName("participants")]
    public List<string>? Participants { get; set; }

    /// <summary>
    /// Block index when the feed was created.
    /// </summary>
    [JsonPropertyName("createdAtBlock")]
    public long CreatedAtBlock { get; set; }

    /// <summary>
    /// Encryption key generation for Group feeds.
    /// Null for Chat/Personal feeds, integer for Group feeds.
    /// </summary>
    [JsonPropertyName("currentKeyGeneration")]
    public int? CurrentKeyGeneration { get; set; }

    /// <summary>
    /// Returns true if this entry was deserialized from a legacy FEAT-060 format
    /// (only lastBlockIndex field present, no title/participants).
    /// When true, the caller should treat this as a cache miss and repopulate from PostgreSQL.
    /// </summary>
    [JsonIgnore]
    public bool IsLegacyFormat => Title == null && Participants == null;
}
