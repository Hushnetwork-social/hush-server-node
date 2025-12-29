using HushShared.Blockchain.BlockModel;

namespace HushShared.Feeds.Model;

/// <summary>
/// Entity tracking key rotation history for audit and message decryption.
/// Composite key: FeedId + KeyGeneration.
/// </summary>
public record GroupFeedKeyGenerationEntity(
    FeedId FeedId,
    int KeyGeneration,
    BlockIndex ValidFromBlock,
    RotationTrigger RotationTrigger)
{
    public virtual GroupFeed? GroupFeed { get; set; }
    public virtual ICollection<GroupFeedEncryptedKeyEntity> EncryptedKeys { get; set; } = [];
}
