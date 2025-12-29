namespace HushShared.Feeds.Model;

/// <summary>
/// Represents an encrypted key for a specific member during key rotation.
/// EncryptedAesKey is the new AES key encrypted with the member's public encrypt key.
/// </summary>
public record GroupFeedEncryptedKey(
    string MemberPublicAddress,
    string EncryptedAesKey);
