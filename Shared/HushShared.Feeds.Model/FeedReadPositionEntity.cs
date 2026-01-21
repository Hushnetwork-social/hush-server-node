using HushShared.Blockchain.BlockModel;

namespace HushShared.Feeds.Model;

/// <summary>
/// Entity tracking a user's last read position in a feed.
/// Used for cross-device read sync - stores the block index up to which the user has read.
/// Composite key: UserId + FeedId.
/// </summary>
/// <param name="UserId">User's public signing address (string, not database ID).</param>
/// <param name="FeedId">Feed GUID as FeedId value type.</param>
/// <param name="LastReadBlockIndex">Block index up to which the user has read messages.</param>
/// <param name="UpdatedAt">UTC timestamp of the last update.</param>
public record FeedReadPositionEntity(
    string UserId,
    FeedId FeedId,
    BlockIndex LastReadBlockIndex,
    DateTime UpdatedAt);
