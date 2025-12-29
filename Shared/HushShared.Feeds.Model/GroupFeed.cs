using HushShared.Blockchain.BlockModel;

namespace HushShared.Feeds.Model;

/// <summary>
/// Entity representing a Group Feed with encrypted messaging and key rotation.
/// </summary>
public record GroupFeed(
    FeedId FeedId,
    string Title,
    string Description,
    bool IsPublic,
    BlockIndex CreatedAtBlock,
    int CurrentKeyGeneration)
{
    public virtual ICollection<GroupFeedParticipantEntity> Participants { get; set; } = [];
    public virtual ICollection<GroupFeedKeyGenerationEntity> KeyGenerations { get; set; } = [];
}
