using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;

namespace HushNode.Feeds.Events;

/// <summary>
/// Published when a member leaves a Group Feed.
/// Used by Protocol Omega to revoke the member's commitment in the Merkle tree.
/// </summary>
public class MemberLeftGroupFeedEvent
{
    public FeedId FeedId { get; }
    public string MemberPublicAddress { get; }
    public BlockIndex BlockIndex { get; }

    public MemberLeftGroupFeedEvent(
        FeedId feedId,
        string memberPublicAddress,
        BlockIndex blockIndex)
    {
        FeedId = feedId;
        MemberPublicAddress = memberPublicAddress;
        BlockIndex = blockIndex;
    }
}
