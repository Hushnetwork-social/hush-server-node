using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;

namespace HushNode.Events;

/// <summary>
/// Event published when a user leaves a group feed.
/// Used by the cache service to remove the user from the cached participants list.
/// </summary>
public class UserLeftGroupEvent(FeedId feedId, string userPublicAddress, BlockIndex blockIndex)
{
    /// <summary>
    /// The ID of the group feed the user left.
    /// </summary>
    public FeedId FeedId { get; } = feedId;

    /// <summary>
    /// The public signing address of the user who left.
    /// </summary>
    public string UserPublicAddress { get; } = userPublicAddress;

    /// <summary>
    /// The block index when the leave occurred.
    /// </summary>
    public BlockIndex BlockIndex { get; } = blockIndex;
}
