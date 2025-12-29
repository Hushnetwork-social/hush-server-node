namespace HushShared.Feeds.Model;

/// <summary>
/// Entity storing each member's ECIES-encrypted copy of the group AES key.
/// Composite key: FeedId + KeyGeneration + MemberPublicAddress.
/// </summary>
public record GroupFeedEncryptedKeyEntity(
    FeedId FeedId,
    int KeyGeneration,
    string MemberPublicAddress,
    string EncryptedAesKey)
{
    public virtual GroupFeedKeyGenerationEntity? KeyGenerationEntity { get; set; }
}
