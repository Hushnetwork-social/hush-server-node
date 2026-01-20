using HushShared.Feeds.Model;

namespace HushNode.Events;

/// <summary>
/// Event published when a user is banned from a group feed.
/// Used by the cache service to remove the user from the cached participants list.
/// </summary>
public class UserBannedFromGroupEvent(FeedId feedId, string userPublicAddress, long blockIndex)
{
    /// <summary>
    /// The ID of the group feed from which the user was banned.
    /// </summary>
    public FeedId FeedId { get; } = feedId;

    /// <summary>
    /// The public signing address of the user who was banned.
    /// </summary>
    public string UserPublicAddress { get; } = userPublicAddress;

    /// <summary>
    /// The block index when the ban occurred.
    /// </summary>
    public long BlockIndex { get; } = blockIndex;
}
