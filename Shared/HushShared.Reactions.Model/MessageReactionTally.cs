using HushShared.Feeds.Model;

namespace HushShared.Reactions.Model;

/// <summary>
/// Aggregated encrypted tally per message.
/// Each array contains 6 elements (one per emoji type).
/// </summary>
public record MessageReactionTally(
    FeedMessageId MessageId,
    FeedId FeedId,
    byte[][] TallyC1X,      // 6 × 32 bytes (X coords of C1 points)
    byte[][] TallyC1Y,      // 6 × 32 bytes (Y coords of C1 points)
    byte[][] TallyC2X,      // 6 × 32 bytes (X coords of C2 points)
    byte[][] TallyC2Y,      // 6 × 32 bytes (Y coords of C2 points)
    int TotalCount,
    long Version,           // Monotonic counter for sync - increments on every update
    DateTime LastUpdated);
