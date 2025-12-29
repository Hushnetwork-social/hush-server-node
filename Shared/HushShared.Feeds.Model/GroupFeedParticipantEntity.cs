using HushShared.Blockchain.BlockModel;

namespace HushShared.Feeds.Model;

/// <summary>
/// Entity tracking group membership, roles, and key access.
/// Composite key: FeedId + ParticipantPublicAddress.
/// </summary>
public record GroupFeedParticipantEntity(
    FeedId FeedId,
    string ParticipantPublicAddress,
    ParticipantType ParticipantType,
    BlockIndex JoinedAtBlock,
    BlockIndex? LeftAtBlock = null,
    BlockIndex? LastLeaveBlock = null)
{
    public virtual GroupFeed? GroupFeed { get; set; }
}
