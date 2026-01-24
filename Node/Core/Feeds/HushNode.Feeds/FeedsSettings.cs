namespace HushNode.Feeds;

/// <summary>
/// Configuration settings for the Feeds module.
/// </summary>
public class FeedsSettings
{
    /// <summary>
    /// Configuration section name in appsettings.
    /// </summary>
    public const string SectionName = "Feeds";

    /// <summary>
    /// Maximum number of messages to return in a single paginated response.
    /// Client-requested limits exceeding this value will be silently capped.
    /// </summary>
    public int MaxMessagesPerResponse { get; set; } = 100;
}
