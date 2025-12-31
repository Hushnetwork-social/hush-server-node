using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;

namespace HushNode.Feeds.Events;

/// <summary>
/// Published when a member is unbanned from a Group Feed.
/// Used by Protocol Omega to register a new commitment in the Merkle tree.
/// </summary>
public class MemberUnbannedFromGroupFeedEvent
{
    public FeedId FeedId { get; }
    public string MemberPublicAddress { get; }
    public int KeyGeneration { get; }
    public BlockIndex BlockIndex { get; }

    public MemberUnbannedFromGroupFeedEvent(
        FeedId feedId,
        string memberPublicAddress,
        int keyGeneration,
        BlockIndex blockIndex)
    {
        FeedId = feedId;
        MemberPublicAddress = memberPublicAddress;
        KeyGeneration = keyGeneration;
        BlockIndex = blockIndex;
    }
}
