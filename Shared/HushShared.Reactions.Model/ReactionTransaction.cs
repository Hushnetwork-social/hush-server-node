using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;

namespace HushShared.Reactions.Model;

/// <summary>
/// Full transaction log for blockchain replay.
/// </summary>
public record ReactionTransaction(
    Guid Id,
    BlockIndex BlockHeight,
    FeedId FeedId,
    FeedMessageId MessageId,
    byte[] Nullifier,           // 32 bytes
    byte[][] CiphertextC1X,     // 6 × 32 bytes
    byte[][] CiphertextC1Y,     // 6 × 32 bytes
    byte[][] CiphertextC2X,     // 6 × 32 bytes
    byte[][] CiphertextC2Y,     // 6 × 32 bytes
    byte[] ZkProof,             // ~256 bytes (Groth16 proof)
    string CircuitVersion,      // e.g., "omega-v1.0.0"
    DateTime CreatedAt);
