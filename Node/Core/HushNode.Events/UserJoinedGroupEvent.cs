using HushShared.Feeds.Model;

namespace HushNode.Events;

/// <summary>
/// Event published when a user successfully joins a group feed.
/// Used by the cache service to add the user to the cached participants list.
/// </summary>
public class UserJoinedGroupEvent(FeedId feedId, string userPublicAddress, long blockIndex)
{
    /// <summary>
    /// The ID of the group feed the user joined.
    /// </summary>
    public FeedId FeedId { get; } = feedId;

    /// <summary>
    /// The public signing address of the user who joined.
    /// </summary>
    public string UserPublicAddress { get; } = userPublicAddress;

    /// <summary>
    /// The block index when the join occurred.
    /// </summary>
    public long BlockIndex { get; } = blockIndex;
}
