using System.Security.Cryptography;
using System.Text;
using HushShared.Feeds.Model;

namespace HushNode.Reactions;

/// <summary>
/// Implementation of reaction key derivation using HKDF-SHA256.
/// Derives per-message and per-feed keys from the Group Feed's AES key.
/// </summary>
public class ReactionKeyDerivationService : IReactionKeyDerivationService
{
    // Constants for HKDF info parameters
    private static readonly byte[] ReactionKeyInfo =
        Encoding.UTF8.GetBytes("protocol_omega/group_reaction/v1");
    private static readonly byte[] FeedSecretInfo =
        Encoding.UTF8.GetBytes("protocol_omega/feed_pk/v1");

    private const int OutputLength = 32;  // 256 bits

    /// <summary>
    /// Derives a per-message reaction key from the group's AES key.
    /// Uses HKDF-SHA256 with the message ID as salt.
    /// </summary>
    public byte[] DeriveReactionKey(byte[] groupAesKey, FeedMessageId messageId)
    {
        ArgumentNullException.ThrowIfNull(groupAesKey);
        if (groupAesKey.Length != 32)
        {
            throw new ArgumentException("Group AES key must be 32 bytes", nameof(groupAesKey));
        }

        // Use messageId GUID bytes as salt
        var salt = messageId.Value.ToByteArray();

        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: groupAesKey,
            outputLength: OutputLength,
            salt: salt,
            info: ReactionKeyInfo);
    }

    /// <summary>
    /// Derives a feed-level secret from the group's AES key.
    /// Uses HKDF-SHA256 with the feed ID as salt.
    /// </summary>
    public byte[] DeriveFeedSecret(byte[] groupAesKey, FeedId feedId)
    {
        ArgumentNullException.ThrowIfNull(groupAesKey);
        if (groupAesKey.Length != 32)
        {
            throw new ArgumentException("Group AES key must be 32 bytes", nameof(groupAesKey));
        }

        // Use feedId GUID bytes as salt
        var salt = feedId.Value.ToByteArray();

        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: groupAesKey,
            outputLength: OutputLength,
            salt: salt,
            info: FeedSecretInfo);
    }
}
