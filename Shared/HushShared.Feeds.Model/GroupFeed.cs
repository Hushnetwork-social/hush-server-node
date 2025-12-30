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
    /// <summary>
    /// Indicates if the group has been soft-deleted.
    /// Deleted groups cannot accept new messages but existing data is preserved.
    /// </summary>
    public bool IsDeleted { get; set; } = false;

    public virtual ICollection<GroupFeedParticipantEntity> Participants { get; set; } = [];
    public virtual ICollection<GroupFeedKeyGenerationEntity> KeyGenerations { get; set; } = [];
}
