using HushShared.Blockchain.BlockModel;

namespace HushShared.Feeds.Model;

/// <summary>
/// Read model for owner circle projections used by Following page circle cards.
/// </summary>
public sealed record CustomCircleSummary(
    FeedId FeedId,
    string Name,
    bool IsInnerCircle,
    int MemberCount,
    int CurrentKeyGeneration,
    BlockIndex CreatedAtBlock,
    BlockIndex? LastUpdatedAtBlock);
