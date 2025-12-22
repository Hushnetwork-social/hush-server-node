using HushShared.Feeds.Model;

namespace HushShared.Reactions.Model;

/// <summary>
/// Nullifier registry for duplicate detection and reaction updates.
/// Stores the previous vote so it can be subtracted when updating.
/// </summary>
public record ReactionNullifier(
    byte[] Nullifier,           // 32 bytes (Poseidon hash) - PRIMARY KEY
    FeedMessageId MessageId,
    byte[][] VoteC1X,           // 6 × 32 bytes
    byte[][] VoteC1Y,           // 6 × 32 bytes
    byte[][] VoteC2X,           // 6 × 32 bytes
    byte[][] VoteC2Y,           // 6 × 32 bytes
    byte[]? EncryptedEmojiBackup,  // ~32 bytes for cross-device recovery
    DateTime CreatedAt,
    DateTime UpdatedAt);
