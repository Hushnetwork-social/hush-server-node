using HushShared.Feeds.Model;

namespace HushNode.Reactions;

/// <summary>
/// Service for deriving reaction keys using HKDF.
/// Used by Protocol Omega to derive per-message reaction keys from Group Feed AES keys.
/// </summary>
public interface IReactionKeyDerivationService
{
    /// <summary>
    /// Derives a per-message reaction key from the group's AES key.
    /// Used for encrypting reactions to a specific message.
    /// </summary>
    /// <param name="groupAesKey">The Group Feed's current AES-256 key (32 bytes)</param>
    /// <param name="messageId">The message ID to derive the key for</param>
    /// <returns>32-byte derived key</returns>
    byte[] DeriveReactionKey(byte[] groupAesKey, FeedMessageId messageId);

    /// <summary>
    /// Derives a feed-level secret from the group's AES key.
    /// Used for computing the feed public key for ZK proofs.
    /// </summary>
    /// <param name="groupAesKey">The Group Feed's current AES-256 key (32 bytes)</param>
    /// <param name="feedId">The feed ID to derive the secret for</param>
    /// <returns>32-byte derived secret</returns>
    byte[] DeriveFeedSecret(byte[] groupAesKey, FeedId feedId);
}
