using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;

namespace HushNode.Feeds.Events;

/// <summary>
/// Published when a member is banned from a Group Feed.
/// Used by Protocol Omega to revoke the member's commitment in the Merkle tree.
/// </summary>
public class MemberBannedFromGroupFeedEvent
{
    public FeedId FeedId { get; }
    public string MemberPublicAddress { get; }
    public BlockIndex BlockIndex { get; }

    public MemberBannedFromGroupFeedEvent(
        FeedId feedId,
        string memberPublicAddress,
        BlockIndex blockIndex)
    {
        FeedId = feedId;
        MemberPublicAddress = memberPublicAddress;
        BlockIndex = blockIndex;
    }
}
