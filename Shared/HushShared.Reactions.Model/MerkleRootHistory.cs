using HushShared.Feeds.Model;

namespace HushShared.Reactions.Model;

/// <summary>
/// History of Merkle roots for grace period verification.
/// </summary>
public record MerkleRootHistory(
    int Id,
    FeedId FeedId,
    byte[] MerkleRoot,          // 32 bytes
    long BlockHeight,
    DateTime CreatedAt);
