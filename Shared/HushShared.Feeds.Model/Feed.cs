using HushShared.Blockchain.BlockModel;

namespace HushShared.Feeds.Model;

public record Feed(
    FeedId FeedId,
    string Alias,
    FeedType FeedType,
    BlockIndex BlockIndex)
{
    public virtual ICollection<FeedParticipant> Participants { get; set; } = [];
}
